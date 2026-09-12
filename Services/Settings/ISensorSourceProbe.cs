namespace GameLauncher.Services.Settings
{
    /// <summary>
    /// Prueft, ob die Sensorquelle - eine laufende LibreHardwareMonitor-Anwendung
    /// mit aktivem Webserver - erreichbar ist.
    ///
    /// Die Pruefung steckt hinter einer Schnittstelle, weil sie eine echte
    /// Netzwerkanfrage stellt: ohne sie haenge jede Erzeugung des
    /// Einstellungsfensters am Netz, und die Anzeige liesse sich nicht pruefen,
    /// ohne dass auf dem ausfuehrenden Rechner tatsaechlich eine Anwendung
    /// laeuft.
    /// </summary>
    internal interface ISensorSourceProbe
    {
        bool IsAvailable();
    }
}
