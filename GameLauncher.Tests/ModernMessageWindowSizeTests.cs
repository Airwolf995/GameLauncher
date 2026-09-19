using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameLauncher.Services.Localization;

namespace GameLauncher.Tests;

/// <summary>
/// Das Meldungsfenster hatte eine feste Hoehe von 200 Pixeln. Laengere
/// Meldungen wurden dadurch abgeschnitten und liessen sich nur durch Scrollen
/// lesen - eine Meldung, die ihre eigene Meldung verbirgt. Betroffen waren
/// unter anderem die Rueckfragen rund um das Einspielen einer Sicherung.
/// </summary>
public sealed class ModernMessageWindowSizeTests
{
    /// <summary>
    /// Die laengste Meldung des Katalogs. Passt diese, passen die uebrigen erst
    /// recht.
    /// </summary>
    private static string LongestMessage =>
        LocalizedTextCatalog.GetTexts(AppLanguage.German)["Settings.ImportConfigRestartBody"];

    [Fact]
    public void LangeMeldungIstOhneScrollenVollstaendigSichtbar()
    {
        RunInSta(() =>
        {
            var window = new ModernMessageWindow(
                LongestMessage,
                "Sicherung einspielen",
                ModernMessageWindow.ModernMessageButton.YesNo);

            try
            {
                // Ohne Anzeigen misst WPF das Layout nicht.
                window.Opacity = 0;
                window.ShowInTaskbar = false;
                window.Show();
                window.UpdateLayout();

                var scrollViewer = FindScrollViewer(window);
                Assert.NotNull(scrollViewer);
                Assert.True(
                    scrollViewer!.ExtentHeight <= scrollViewer.ViewportHeight + 0.5,
                    $"Die Meldung braucht {scrollViewer.ExtentHeight:F0} Pixel, sichtbar sind nur " +
                    $"{scrollViewer.ViewportHeight:F0}. Sie muesste gescrollt werden.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Sehr lange Meldungen sollen das Fenster nicht ueber den Bildschirmrand
    /// hinaus wachsen lassen; dann greift wieder das Scrollen.
    /// </summary>
    [Fact]
    public void SehrLangeMeldungSprengtDasFensterNicht()
    {
        RunInSta(() =>
        {
            var window = new ModernMessageWindow(
                string.Join(" ", Enumerable.Repeat("Sehr langer Meldungstext.", 200)),
                "Titel",
                ModernMessageWindow.ModernMessageButton.OK);

            try
            {
                window.Opacity = 0;
                window.ShowInTaskbar = false;
                window.Show();
                window.UpdateLayout();

                Assert.True(
                    window.ActualHeight <= window.MaxHeight,
                    $"Das Fenster ist mit {window.ActualHeight:F0} Pixeln hoeher als erlaubt.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var found = FindScrollViewer(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void RunInSta(Action action)
    {
        Exception? capturedException = null;
        using var finished = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
            finally
            {
                finished.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), "Das Fenster antwortete nicht rechtzeitig.");
        thread.Join();

        if (capturedException != null)
        {
            throw capturedException;
        }
    }
}
