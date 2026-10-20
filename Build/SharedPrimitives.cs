using System;
using System.Collections.Generic;
using System.Text;

namespace Cnidaria;

///<summary>Describes a source diagnostic</summary>
public interface IDiagnostic
{
    DiagnosticSeverity Severity { get; }
    string Message { get; }
    TextSpan Position { get; }
    string GetMessage(string source);
}

///<summary>Represents a source range</summary>
public readonly struct TextSpan
{
    public readonly int Start;
    public readonly int Length;

    public int End => Start + Length;

    public TextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public static bool operator ==(TextSpan left, TextSpan right) => left.Start == right.Start && left.Length == right.Length;
    public static bool operator !=(TextSpan left, TextSpan right) => !(left == right);
    public override string ToString() => $"[{Start}..{End})";
    public string ToString(string souce)
    {
        var res = GetLineAndColumn(souce, Start);
        return $"[{res.line}, {res.column}]";
    }
    public override bool Equals(object? obj)
    {
        return obj != null && obj is TextSpan t && t.Start == this.Start && t.Length == this.Length;
    }
    public override int GetHashCode()
    {
        return HashCode.Combine(Start, Length);
    }

    public static (int line, int column) GetLineAndColumn(string text, int index)
    {
        if (text == null)
            throw new ArgumentNullException(nameof(text));

        if ((uint)index > (uint)text.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        int line = 1;
        int column = 1;

        for (int i = 0; i < index; i++)
        {
            char c = text[i];

            if (c == '\n')
            {
                line++;
                column = 1;
            }
            else if (c == '\r')
            {
                line++;
                column = 1;

                if (i + 1 < index && text[i + 1] == '\n')
                    i++;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }
}


public enum SyntaxColor : byte
{

}
public readonly struct ColorSpan
{
    public TextSpan Span { get; }
    public SyntaxColor Color { get; }
    public ColorSpan(TextSpan span, SyntaxColor color)
    {
        Span = span;
        Color = color;
    }
    public bool Equals(ColorSpan other)
        => other.Span.Start == this.Span.Start && other.Span.Length == this.Span.Length && other.Color == this.Color;

    public override bool Equals(object? obj) => obj is ColorSpan other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Span, Color);
    public static bool operator ==(ColorSpan left, ColorSpan right) => left.Equals(right);
    public static bool operator !=(ColorSpan left, ColorSpan right) => !left.Equals(right);
}