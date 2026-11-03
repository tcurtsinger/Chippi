// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Text.Json;
using Chippi.Features.AgentUsage.Sessions;

namespace Chippi.Features.AgentUsage.Readers;

internal sealed partial class CodexLogs
{
    // Replay complete records in order. A desktop question can be answered in a later turn.
    private sealed class LogState
    {
        private const string ReplyTag = "send_user_message_question_reply";
        private sealed record Question(DateTimeOffset Since, bool Async, bool Accepted, int[] Remaining);
        private readonly Dictionary<string, Question> _questions;
        private Turn? _turn;

        public LogState() => _questions = new(StringComparer.Ordinal);
        public LogState(LogState prior)
        {
            _questions = new(prior._questions, StringComparer.Ordinal);
            _turn = prior._turn;
            Limits = prior.Limits;
        }

        public UsageReading? Limits { get; private set; }

        public Turn? Current
        {
            get
            {
                var since = _questions.Values.Where(q => q.Accepted).Select(q => (DateTimeOffset?)q.Since).Min();
                return since is { } at ? new(AgentSessionState.NeedsInput, at) : _turn;
            }
        }

        public void InspectLine(string line)
        {
            if (!line.Contains("\"task_started\"", StringComparison.Ordinal)
                && !line.Contains("\"task_complete\"", StringComparison.Ordinal)
                && !line.Contains("\"turn_aborted\"", StringComparison.Ordinal)
                && !line.Contains("\"rate_limits\"", StringComparison.Ordinal)
                && !line.Contains("request_user_input", StringComparison.Ordinal)
                && !(_questions.Count != 0 && (line.Contains(ReplyTag, StringComparison.Ordinal)
                    || line.Contains("\"function_call_output\"", StringComparison.Ordinal))))
                return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("payload", out var payload)
                    || payload.ValueKind != JsonValueKind.Object || !root.TryGetProperty("timestamp", out var ts)
                    || ts.ValueKind != JsonValueKind.String || !ts.TryGetDateTimeOffset(out var at))
                    return;
                switch (Text(root, "type"))
                {
                    case "event_msg": ReadEvent(payload, at); break;
                    case "response_item": ReadItem(payload, at); break;
                }
            }
            catch (JsonException) { } // Ignore malformed records, including embedded tool JSON.
        }

        private void ReadEvent(JsonElement payload, DateTimeOffset at)
        {
            switch (Text(payload, "type"))
            {
                case "task_started":
                    _turn = new(AgentSessionState.Working, at);
                    break;
                case "task_complete":
                    _turn = new(AgentSessionState.Idle, at);
                    foreach (var id in _questions.Where(q => !q.Value.Async || !q.Value.Accepted).Select(q => q.Key).ToArray())
                        _questions.Remove(id);
                    break;
                case "turn_aborted":
                    _turn = new(AgentSessionState.Idle, at);
                    _questions.Clear();
                    break;
                case "token_count" when payload.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object:
                    var reading = CodexLimits.Parse(limits, at);
                    if (reading.Windows.Count != 0) Limits = reading;
                    break;
            }
        }

        private void ReadItem(JsonElement payload, DateTimeOffset at)
        {
            switch (Text(payload, "type"))
            {
                case "function_call": Request(payload, at); break;
                case "function_call_output": Result(payload); break;
                case "message" when Text(payload, "role") == "user": Reply(payload); break;
            }
        }

        private static string? Text(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private void Request(JsonElement payload, DateTimeOffset at)
        {
            string? name = Text(payload, "name");
            if (name is not ("request_user_input" or "request_user_input_async") || Text(payload, "call_id") is not { Length: > 0 } id
                || Text(payload, "arguments") is not { } arguments)
                return;
            using var args = JsonDocument.Parse(arguments);
            if (args.RootElement.ValueKind != JsonValueKind.Object || !args.RootElement.TryGetProperty("questions", out var questions)
                || questions.ValueKind != JsonValueKind.Array || questions.GetArrayLength() == 0)
                return;
            bool asyncRequest = name == "request_user_input_async";
            _questions[id] = new(at, asyncRequest, !asyncRequest, Enumerable.Range(0, questions.GetArrayLength()).ToArray());
        }

        private void Result(JsonElement payload)
        {
            if (Text(payload, "call_id") is not { } id || !_questions.Remove(id, out var question) || !question.Async)
                return;
            // request_user_input_async returns a delivery acknowledgment before the person responds.
            if (Text(payload, "output") is not { } json) return;
            using var result = JsonDocument.Parse(json);
            if (result.RootElement.ValueKind == JsonValueKind.Object && result.RootElement.TryGetProperty("accepted", out var accepted)
                && accepted.ValueKind == JsonValueKind.True)
                _questions[id] = question with { Accepted = true };
        }

        private void Reply(JsonElement payload)
        {
            if (!payload.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || Text(part, "type") != "input_text" || Text(part, "text") is not { } text)
                    continue;
                const string open = "<" + ReplyTag + ">", close = "</" + ReplyTag + ">";
                string envelope = text.Trim();
                if (!envelope.StartsWith(open, StringComparison.Ordinal) || !envelope.EndsWith(close, StringComparison.Ordinal))
                    continue;
                using var replies = JsonDocument.Parse(envelope[open.Length..^close.Length]);
                if (replies.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var reply in replies.RootElement.EnumerateArray())
                {
                    if (reply.ValueKind != JsonValueKind.Object || Text(reply, "questionItemId") is not { } key) continue;
                    using var parts = JsonDocument.Parse(key);
                    var fields = parts.RootElement;
                    if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() != 3
                        || fields[0].ValueKind != JsonValueKind.String || fields[0].GetString() != "request_user_input_async"
                        || fields[1].ValueKind != JsonValueKind.String || fields[1].GetString() is not { } id
                        || fields[2].ValueKind != JsonValueKind.Number || !fields[2].TryGetInt32(out int index)
                        || !_questions.TryGetValue(id, out var question) || !question.Async || !question.Remaining.Contains(index))
                        continue;
                    var remaining = question.Remaining.Where(i => i != index).ToArray();
                    if (remaining.Length == 0) _questions.Remove(id);
                    else _questions[id] = question with { Remaining = remaining };
                }
            }
        }
    }
}
