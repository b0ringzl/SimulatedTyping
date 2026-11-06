namespace SimulatedTyping;

internal sealed class TypingEngine
{
    private static readonly Dictionary<string, string> HomophoneWords = new()
    {
        ["已经"] = "一经", ["一经"] = "已经",
        ["必须"] = "必需", ["必需"] = "必须",
        ["联系"] = "练习", ["练习"] = "联系",
        ["权利"] = "权力", ["权力"] = "权利",
        ["检查"] = "检察", ["检察"] = "检查"
    };

    private static readonly Dictionary<char, char> Homophones = new()
    {
        ['在'] = '再', ['再'] = '在', ['的'] = '地', ['地'] = '的',
        ['得'] = '的', ['已'] = '以', ['以'] = '已', ['是'] = '事',
        ['事'] = '是', ['做'] = '作', ['作'] = '做', ['向'] = '像',
        ['像'] = '向', ['买'] = '卖', ['卖'] = '买'
    };

    private readonly IReadOnlyList<TypingUnit> _units;
    private readonly TypingOptions _options;
    private readonly IntPtr _target;
    private readonly Action<string, int, int> _notify;
    private readonly Random _random = new();
    private readonly int _total;
    private bool _paused;
    private double _currentSpeed;
    private int _completed;

    public bool IsPaused => _paused;

    public TypingEngine(IReadOnlyList<TypingUnit> units, TypingOptions options, IntPtr target,
                        Action<string, int, int> notify)
    {
        _units = units;
        _options = options;
        _target = target;
        _notify = notify;
        _total = units.Sum(u => u.Text.Length);
        _currentSpeed = options.UseSpeedRange ? (options.MinSpeed + options.MaxSpeed) / 2.0 : options.FixedSpeed;
    }

    public void TogglePause()
    {
        _paused = !_paused;
        Report(_paused ? "已暂停；返回原窗口后按 Ctrl+F8 继续" : "正在输入");
    }

    public async Task RunAsync(CancellationToken token)
    {
        Report("正在输入");
        for (var i = 0; i < _units.Count; i++)
        {
            var unit = _units[i];
            await EnsureReady(token);
            var cpm = NextSpeed();
            if (unit.Kind == UnitKind.Hanzi && _random.Next(100) < _options.ErrorRate)
            {
                if (_random.Next(100) < 45 && TryHomophone(unit.Text, out var wrong))
                    await TypeHomophoneCorrection(unit, wrong, cpm, token);
                else if (_options.NativePinyin && unit.Pinyin.Length >= 3)
                    await TypeNativePinyin(unit, cpm, token, typo: true);
                else
                    await TypeVisibleCorrection(unit, cpm, token);
            }
            else await TypeUnit(unit, cpm, token);

            _completed += unit.Text.Length;
            Report("正在输入");
            if (unit.Text is "。" or "！" or "？" or "!" or "?")
                await Delay(450 + _random.Next(450), token);
            else if (unit.Text is "，" or "、" or "," or ";" or "；")
                await Delay(170 + _random.Next(250), token);

            var last = i == _units.Count - 1;
            var paragraphEnd = (unit.Kind == UnitKind.Newline && i > 0 && _units[i - 1].Kind != UnitKind.Newline) ||
                               (last && unit.Kind != UnitKind.Newline);
            var sentenceEnd = !paragraphEnd && IsSentenceEnd(unit.Text) &&
                              (last || _units[i + 1].Kind != UnitKind.Newline);
            var rate = paragraphEnd ? _options.ParagraphPauseRate :
                       sentenceEnd ? _options.SentencePauseRate : 0;
            if (_random.Next(100) < rate)
                await SavePause(paragraphEnd ? "段末" : "句末", token);
        }
        Report("已完成；请核对目标文本");
    }

    private async Task TypeUnit(TypingUnit unit, double cpm, CancellationToken token)
    {
        if (unit.Kind == UnitKind.Newline)
        {
            if (_options.NewlineMode == 0) KeyboardInput.ShiftEnter();
            else if (_options.NewlineMode == 1) KeyboardInput.Key(KeyboardInput.VkReturn);
            await Delay(350, token);
        }
        else if (unit.Kind == UnitKind.Hanzi && _options.NativePinyin)
            await TypeNativePinyin(unit, cpm, token, typo: false);
        else await TypeDirect(unit.Text, cpm, token);
    }

    private async Task TypeNativePinyin(TypingUnit unit, double cpm, CancellationToken token, bool typo)
    {
        var pinyin = unit.Pinyin.Replace(",", "");
        var strokes = pinyin.Length + 1 + (typo ? 2 : 0);
        var delay = 60000.0 / cpm * unit.Text.Length / strokes;
        var typoAt = typo ? _random.Next(pinyin.Length) : -1;
        for (var i = 0; i < pinyin.Length; i++)
        {
            var letter = pinyin[i];
            await EnsureReady(token);
            if (i == typoAt)
            {
                KeyboardInput.LatinLetter(WrongKey(letter));
                await KeyDelay(delay, token);
                await Delay(170 + _random.Next(230), token);
                KeyboardInput.Key(KeyboardInput.VkBack);
                await KeyDelay(delay, token);
            }
            KeyboardInput.LatinLetter(letter);
            await KeyDelay(delay, token);
        }
        await EnsureReady(token);
        // Candidate order is user- and IME-specific. Show genuine pinyin composition,
        // cancel it, then commit the planned word exactly instead of accepting candidate 1.
        KeyboardInput.Key(KeyboardInput.VkEscape);
        await Delay(45, token);
        await EnsureReady(token);
        foreach (var c in unit.Text)
            KeyboardInput.Character(c);
    }

    private async Task TypeHomophoneCorrection(TypingUnit unit, string wrong, double cpm, CancellationToken token)
    {
        // Commit the chosen wrong word explicitly; IME candidate ranks differ by machine.
        await TypeDirect(wrong, cpm, token);
        await Delay(700 + _random.Next(1000), token);
        foreach (var _ in wrong)
        {
            await EnsureReady(token);
            KeyboardInput.Key(KeyboardInput.VkBack);
            await Delay(70 + _random.Next(70), token);
        }
        await Delay(200 + _random.Next(300), token);
        await TypeUnit(unit, cpm, token);
    }

    private async Task TypeVisibleCorrection(TypingUnit unit, double cpm, CancellationToken token)
    {
        // A doubled last character is a common visible overtype in direct mode.
        await TypeDirect(unit.Text + unit.Text[^1], cpm, token);
        await Delay(300 + _random.Next(450), token);
        await EnsureReady(token);
        KeyboardInput.Key(KeyboardInput.VkBack);
        await Delay(130, token);
    }

    private async Task TypeDirect(string text, double cpm, CancellationToken token)
    {
        var delay = 60000.0 / cpm;
        foreach (var c in text)
        {
            await EnsureReady(token);
            KeyboardInput.Character(c);
            await KeyDelay(delay, token);
        }
    }

    private async Task EnsureReady(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (KeyboardInput.GetForegroundWindow() != _target && !_paused)
            {
                _paused = true;
                Report("目标窗口已切换；返回原窗口后按 Ctrl+F8 继续");
            }
            if (!_paused && KeyboardInput.GetForegroundWindow() == _target) return;
            await Task.Delay(80, token);
        }
    }

    private double NextSpeed()
    {
        if (!_options.UseSpeedRange) return _options.FixedSpeed;
        var target = _options.MinSpeed + _random.NextDouble() * (_options.MaxSpeed - _options.MinSpeed);
        _currentSpeed = Math.Clamp(_currentSpeed * 0.65 + target * 0.35, _options.MinSpeed, _options.MaxSpeed);
        return _currentSpeed;
    }

    private Task KeyDelay(double baseline, CancellationToken token) =>
        Delay(Math.Clamp((int)(baseline * (0.82 + _random.NextDouble() * 0.36)), 25, 3000), token);

    private static Task Delay(int ms, CancellationToken token) => Task.Delay(ms, token);

    private static bool IsSentenceEnd(string text) => text is "。" or "！" or "？" or "!" or "?" or ".";

    private async Task SavePause(string boundary, CancellationToken token)
    {
        var remaining = (_options.SaveWaitSeconds + 1) * 1000;
        var displayed = -1;
        while (remaining > 0)
        {
            await EnsureReady(token);
            var seconds = (remaining + 999) / 1000;
            if (seconds != displayed)
            {
                Report($"{boundary}停顿，等待保存：{seconds} 秒；Ctrl+F9 可停止");
                displayed = seconds;
            }
            var slice = Math.Min(100, remaining);
            await Delay(slice, token);
            if (!_paused && KeyboardInput.GetForegroundWindow() == _target)
                remaining -= slice;
        }
        Report("正在输入");
    }

    private static bool TryHomophone(string text, out string wrong)
    {
        if (HomophoneWords.TryGetValue(text, out var alternate))
        {
            wrong = alternate;
            return true;
        }
        for (var i = 0; i < text.Length; i++)
        {
            if (Homophones.TryGetValue(text[i], out var replacement))
            {
                wrong = text[..i] + replacement + text[(i + 1)..];
                return true;
            }
        }
        wrong = "";
        return false;
    }

    private static char WrongKey(char key) => key == 'n' ? 'm' : 'n';

    private void Report(string status) => _notify(status, _completed, _total);
}
