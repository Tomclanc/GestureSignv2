using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GestureSign.Foundation.Steps;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Colors;
using TextGetOptions = Microsoft.UI.Text.TextGetOptions;
using TextSetOptions = Microsoft.UI.Text.TextSetOptions;

namespace GestureSign.WinUI;

/// <summary>
/// Text fields of the action editor: "{variable}" tokens are shown in the accent colour, using
/// exactly the spans the runner would replace (see <see cref="StepRunner.FindTokens"/>), so JSON
/// braces and "{{" stay plain.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan HighlightDelay = TimeSpan.FromMilliseconds(220);

    private sealed class VariableColoring
    {
        public VariableColoring(Microsoft.UI.Dispatching.DispatcherQueueTimer timer) => Timer = timer;
        public Microsoft.UI.Dispatching.DispatcherQueueTimer Timer { get; }
        /// <summary>Spans currently drawn in the accent colour, to be reset when they stop being tokens.</summary>
        public List<(int Start, int Length)> Painted { get; } = new();
    }

    private readonly ConditionalWeakTable<RichEditBox, VariableColoring> _variableColoring = new();

    /// <summary>A plain-text editor that colours {variable} tokens; reads and writes the same text a TextBox would.</summary>
    private RichEditBox NewVariableTextEditor(string value, bool multiline, bool mono, double minHeight, string? placeholder = null)
    {
        var box = new RichEditBox
        {
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = minHeight,
            IsSpellCheckEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (mono)
            box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        if (placeholder is not null)
            box.PlaceholderText = placeholder;
        SetVariableText(box, value);
        EnableVariableColoring(box);
        return box;
    }

    /// <summary>Text of a variable editor, without the paragraph mark RichEdit keeps at the end.</summary>
    private static string VariableText(RichEditBox box)
    {
        box.Document.GetText(TextGetOptions.None, out var text);
        return text.EndsWith("\r", StringComparison.Ordinal) ? text[..^1] : text;
    }

    /// <summary>Replaces the whole content of a variable editor (browse, presets).</summary>
    private void SetVariableText(RichEditBox box, string? text)
    {
        box.Document.SetText(TextSetOptions.None, (text ?? "").Replace("\r\n", "\r").Replace("\n", "\r"));
        PaintVariableTokens(box);
    }

    /// <summary>Inserts a token at the caret, replacing the selection.</summary>
    private static void InsertVariableToken(RichEditBox box, string token)
    {
        var document = box.Document;
        var start = Math.Max(0, document.Selection.StartPosition);
        var end = Math.Max(start, document.Selection.EndPosition);
        document.GetRange(start, end).SetText(TextSetOptions.None, token);
        document.Selection.SetRange(start + token.Length, start + token.Length);
        box.Focus(FocusState.Programmatic);
    }

    private void EnableVariableColoring(RichEditBox box)
    {
        var timer = box.DispatcherQueue.CreateTimer();
        timer.Interval = HighlightDelay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => PaintVariableTokens(box);
        var state = new VariableColoring(timer);
        _variableColoring.Add(box, state);
        box.TextChanged += (_, _) =>
        {
            // Typing "{" then a name: wait for a pause so IME composition is not disturbed.
            timer.Stop();
            timer.Start();
        };
        box.ActualThemeChanged += (_, _) => PaintVariableTokens(box);
        box.Unloaded += (_, _) => timer.Stop();
        PaintVariableTokens(box);
    }

    private void PaintVariableTokens(RichEditBox box)
    {
        if (!_variableColoring.TryGetValue(box, out var state))
            return;
        var spans = StepRunner.FindTokens(VariableText(box));
        // Nothing to paint and nothing painted: leave the document alone, so plain typing
        // (and IME composition inside it) is never touched.
        if (spans.Count == 0 && state.Painted.Count == 0)
            return;

        var document = box.Document;
        var start = document.Selection.StartPosition;
        var end = document.Selection.EndPosition;
        var accent = TokenColor();
        var plain = PlainTextColor(box);
        try
        {
            foreach (var (paintedStart, paintedLength) in state.Painted)
                document.GetRange(paintedStart, paintedStart + paintedLength).CharacterFormat.ForegroundColor = plain;
            foreach (var (tokenStart, tokenLength) in spans)
                document.GetRange(tokenStart, tokenStart + tokenLength).CharacterFormat.ForegroundColor = accent;
            state.Painted.Clear();
            state.Painted.AddRange(spans);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            try { document.Selection.SetRange(start, end); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    private static Color PlainTextColor(Control box)
        => (box.Foreground as SolidColorBrush)?.Color
           ?? (box.ActualTheme == ElementTheme.Dark ? White : Black);

    private Color TokenColor()
        => (ThemeBrush("AccentTextFillColorPrimaryBrush") as SolidColorBrush)?.Color ?? DodgerBlue;

    /// <summary>Writes summary text into a TextBlock, drawing {variable} tokens in the accent colour.</summary>
    private void SetSummaryText(TextBlock block, string text, Brush? accent, Brush? plain = null)
    {
        block.Inlines.Clear();
        text ??= "";
        var last = 0;
        foreach (var (start, length) in StepRunner.FindTokens(text))
        {
            if (start > last)
                block.Inlines.Add(new Run { Text = text[last..start], Foreground = plain ?? block.Foreground });
            var run = new Run { Text = text.Substring(start, length) };
            if (accent is not null)
                run.Foreground = accent;
            block.Inlines.Add(run);
            last = start + length;
        }
        if (last < text.Length)
            block.Inlines.Add(new Run { Text = text[last..], Foreground = plain ?? block.Foreground });
    }
}
