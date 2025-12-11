using System;
using System.Windows;
using System.Windows.Threading;

namespace Remotier.Services;

public class ClipboardService
{
    private DispatcherTimer _timer;
    private string _lastText = "";
    private bool _isPaused;

    public event Action<string> ClipboardTextChanged = delegate { };

    public ClipboardService()
    {
        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.0);
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_isPaused) return;

        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (text != _lastText)
                {
                    _lastText = text;
                    ClipboardTextChanged?.Invoke(text);
                }
            }
        }
        catch { }
    }

    public void SetClipboardText(string text)
    {
        if (text == _lastText) return;

        _isPaused = true;
        try
        {
            // Must be on UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(text);
                _lastText = text;
            });
        }
        catch { }
        finally
        {
            // Resume watching after a short delay to avoid echo
            // Actually just setting _lastText is enough to avoid re-triggering current change on next tick
            // if we assume SetText is synchronous.
            _isPaused = false;
        }
    }
}
