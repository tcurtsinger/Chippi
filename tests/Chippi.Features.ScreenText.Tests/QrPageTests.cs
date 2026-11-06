// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Features.ScreenText.Views;
using Chippi.Island.Tests;
using static Chippi.Features.ScreenText.Tests.Shots;

namespace Chippi.Features.ScreenText.Tests;

/// <summary>The island's QR code page (Capture results v3) over a fake model: a link, several codes, Wi-Fi, and the states.</summary>
public sealed class QrPageTests
{
    private static readonly Uri Menu = new("https://menu.example.com/table/12?lang=en");

    private static QrCodeResult Link(int seed = 1) =>
        new(QrKind.Link, Menu.OriginalString, Code(seed)) { Link = Menu };

    private static QrCodeResult Plain(int seed = 2) =>
        new(QrKind.Text, "Guest Wi-Fi: cafe-guest · password on the counter", Code(seed));

    private static QrCodeResult WiFi() =>
        new(QrKind.WiFi, "WIFI:T:WPA;S:cafe-guest;P:espresso-2024;;", Code(3))
        {
            Fields = [new("Name", "cafe-guest"), new("Security", "WPA"), new("Password", "espresso-2024", Secret: true)],
        };

    [Fact]
    public void A_link_shows_its_host_and_opens_only_from_Open_link()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeQr { Codes = [Link()] };
            var view = Laid(new QrResultView(model));
            Assert.Equal(Visibility.Collapsed, view.Pager.Visibility);
            var texts = Texts(view);
            Assert.Contains("Link", texts);
            Assert.Contains("menu.example.com", texts);
            Assert.DoesNotContain(QrResultView.CheckAddress, texts);
            Assert.Contains(Menu.OriginalString, Find<TextBox>(view).Select(b => b.Text));
            Assert.Equal(Visibility.Visible, view.OpenButton.Visibility);
            Assert.Empty(model.Opened);

            Click(view.OpenButton);
            Assert.Equal([Menu], model.Opened);
            Click(view.CopyButton);
            Assert.Equal([Menu.OriginalString], model.Copies);
            Assert.Equal("Copied", view.Status);

            model.Set([Link() with { CheckAddress = true }]);
            Assert.Contains(QrResultView.CheckAddress, Texts(view));
        });
    }

    [Fact]
    public void Several_codes_show_one_at_a_time_and_plain_text_has_Copy_only()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeQr { Codes = [Link(), Plain()] };
            var view = Laid(new QrResultView(model));
            Assert.Equal(Visibility.Visible, view.Pager.Visibility);
            Assert.Contains("1 of 2", Texts(view.Pager));
            Assert.False(view.PreviousButton.IsEnabled);

            Click(view.NextButton);
            Assert.Contains("2 of 2", Texts(view.Pager));
            Assert.False(view.NextButton.IsEnabled);
            Assert.Equal(QrKind.Text, view.Current!.Kind);
            Assert.Equal(Visibility.Collapsed, view.OpenButton.Visibility);
            Click(view.CopyButton);
            Assert.Equal([Plain().Payload], model.Copies);

            model.Refuse = true;
            Click(view.CopyButton);
            Assert.Equal("Copy failed", view.Status);
        });
    }

    [Fact]
    public void A_Wi_Fi_password_shows_only_after_Show()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var view = Laid(new QrResultView(new FakeQr { Codes = [WiFi()] }));
            Assert.Contains("Wi-Fi", Texts(view));
            Assert.Contains(QrResultView.Hidden, Texts(view));
            Assert.DoesNotContain("espresso-2024", Find<TextBox>(view).Select(b => b.Text));

            var show = Find<Button>(view).Single(b => Equals(b.Content, "Show"));
            Click(show);
            Assert.Contains("espresso-2024", Find<TextBox>(view).Select(b => b.Text));
            Assert.Contains(Find<Button>(view), b => Equals(b.Content, "Hide"));
        });
    }

    [Fact]
    public void No_code_and_an_unreadable_code_offer_Reselect()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeQr { State = QrScanState.NotFound };
            var view = Laid(new QrResultView(model));
            Assert.Equal([QrResultView.NotFound], Texts(view.Message));
            Assert.Equal(Visibility.Collapsed, view.Result.Visibility);
            Assert.Equal(Visibility.Collapsed, view.CopyButton.Visibility);
            Click(view.ReselectButton);
            Assert.Equal(1, model.Reselects);

            model.State = QrScanState.Unreadable;
            model.Set([]);
            Assert.Equal([QrResultView.Unreadable, QrResultView.UnreadableFix], Texts(view.Message));
        });
    }

    [Fact]
    public void Shots()
    {
        if (Folder is null) return;
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var link = new QrResultView(new FakeQr { Codes = [Link()] });
            Panel(Laid(link), link.Header, "island-qr-link");
            var several = Laid(new QrResultView(new FakeQr { Codes = [Link(), Plain()] }));
            Click(several.NextButton);
            Panel(several, several.Header, "island-qr-several");
            var wifi = new QrResultView(new FakeQr { Codes = [WiFi()] });
            Panel(Laid(wifi), wifi.Header, "island-qr-wifi");
            var check = new QrResultView(new FakeQr { Codes = [new(QrKind.Link, "https://xn--menu-exmple-9ib.com/table/12", Code(4)) { Link = new Uri("https://xn--menu-exmple-9ib.com/table/12"), CheckAddress = true }] });
            Panel(Laid(check), check.Header, "island-qr-check-address");
            var none = new QrResultView(new FakeQr { State = QrScanState.NotFound });
            Panel(Laid(none), none.Header, "island-qr-none");
            var unreadable = new QrResultView(new FakeQr { State = QrScanState.Unreadable });
            Panel(Laid(unreadable), unreadable.Header, "island-qr-unreadable");
        });
    }

    internal sealed class FakeQr : IQrPageModel
    {
        public IReadOnlyList<QrCodeResult> Codes { get; set; } = [];

        public QrScanState State { get; set; } = QrScanState.Read;

        public bool Refuse { get; set; }

        public List<string> Copies { get; } = [];

        public List<Uri> Opened { get; } = [];

        public int Reselects { get; private set; }

        public event Action? Changed;

        public void Set(IReadOnlyList<QrCodeResult> codes)
        {
            Codes = codes;
            Changed?.Invoke();
        }

        public bool Copy(string payload)
        {
            if (Refuse) return false;
            Copies.Add(payload);
            return true;
        }

        public void Open(Uri link) => Opened.Add(link);

        public void Reselect() => Reselects++;
    }
}
