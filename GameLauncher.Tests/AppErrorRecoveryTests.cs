using System;
using GameLauncher;

namespace GameLauncher.Tests
{
    /// <summary>
    /// Fehler im Oberflaechenstrang beendeten den Launcher kommentarlos: Der
    /// Haken auf <c>AppDomain.UnhandledException</c> meldet einen Absturz nur,
    /// abwenden kann er ihn nicht. Diese Tests sichern die Entscheidung, nach
    /// welchen Fehlern weitergelaufen werden darf.
    /// </summary>
    public class AppErrorRecoveryTests
    {
        /// <summary>
        /// Der Alltagsfall: ein misslungener Klick, etwa weil eine ausgewaehlte
        /// Bilddatei gesperrt ist. Dafuer soll der Launcher nicht schliessen.
        /// </summary>
        [Theory]
        [InlineData(typeof(InvalidOperationException))]
        [InlineData(typeof(System.IO.IOException))]
        [InlineData(typeof(UnauthorizedAccessException))]
        [InlineData(typeof(NullReferenceException))]
        public void AlltagsfehlerLassenDenLauncherWeiterlaufen(Type exceptionType)
        {
            var exception = (Exception)Activator.CreateInstance(exceptionType)!;

            Assert.True(App.IsRecoverable(exception));
        }

        /// <summary>
        /// Bei erschoepftem Speicher oder verletztem Speicherschutz waere
        /// Weiterlaufen ein Blindflug - dort bleibt es beim Absturz.
        /// </summary>
        [Fact]
        public void ErschoepfterSpeicherBeendetDenLauncher()
        {
            Assert.False(App.IsRecoverable(new OutOfMemoryException()));
        }

        [Fact]
        public void VerletzterSpeicherschutzBeendetDenLauncher()
        {
            Assert.False(App.IsRecoverable(new AccessViolationException()));
        }

        [Fact]
        public void OhneFehlerobjektWirdNichtsAbgefangen()
        {
            Assert.False(App.IsRecoverable(null));
        }
    }
}
