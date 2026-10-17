namespace OscarWatch.Core.Terminal.Modem;

/// <summary>Bit clock for NRZ G3RUH baseband. Samples the level each bit period and tracks edges for the PLL.</summary>
public sealed class G3ruhClockRecovery
{
    private readonly int _step;
    private int _pll;
    private bool _prevPositive;

    public G3ruhClockRecovery(int sampleRate)
    {
        _step = (int)Math.Round(4294967296.0 * G3ruhModulator.Baud / sampleRate);
    }

    public void Reset()
    {
        _pll = 0;
        _prevPositive = false;
    }

    /// <summary>Returns -1 when no bit is due, otherwise 0 or 1 for the sliced NRZ level.</summary>
    public int Push(float demod, bool locked)
    {
        var result = -1;
        var previous = _pll;
        _pll = unchecked(_pll + _step);
        if (previous > 0 && _pll < 0)
            result = demod >= 0 ? 1 : 0;

        var positive = demod >= 0;
        if (positive != _prevPositive)
        {
            _pll = (int)(_pll * (locked ? AfskClockRecovery.LockedInertia : AfskClockRecovery.SearchingInertia));
            _prevPositive = positive;
        }

        return result;
    }
}

/// <summary>Turns NRZI line levels into HDLC NRZ bits.</summary>
public sealed class G3ruhNrziDecoder
{
    private bool _lastLevel;
    private bool _haveLevel;

    public void Reset()
    {
        _lastLevel = false;
        _haveLevel = false;
    }

    public bool PushLevel(bool level)
    {
        if (!_haveLevel)
        {
            _lastLevel = level;
            _haveLevel = true;
            return true;
        }

        var hdlcOne = level == _lastLevel;
        _lastLevel = level;
        return hdlcOne;
    }
}
