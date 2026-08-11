namespace System.Drawing
{
    public enum KnownColor
    {
        // This enum is order dependent

        // 0 - reserved for "not a known color"

        // "System" colors, Part 1
        ActiveBorder = 1,
        ActiveCaption,
        ActiveCaptionText,
        AppWorkspace,
        Control,
        ControlDark,
        ControlDarkDark,
        ControlLight,
        ControlLightLight,
        ControlText,
        Desktop,
        GrayText,
        Highlight,
        HighlightText,
        HotTrack,
        InactiveBorder,
        InactiveCaption,
        InactiveCaptionText,
        Info,
        InfoText,
        Menu,
        MenuText,
        ScrollBar,
        Window,
        WindowFrame,
        WindowText,

        // "Web" Colors, Part 1
        Transparent,
        AliceBlue,
        AntiqueWhite,
        Aqua,
        Aquamarine,
        Azure,
        Beige,
        Bisque,
        Black,
        BlanchedAlmond,
        Blue,
        BlueViolet,
        Brown,
        BurlyWood,
        CadetBlue,
        Chartreuse,
        Chocolate,
        Coral,
        CornflowerBlue,
        Cornsilk,
        Crimson,
        Cyan,
        DarkBlue,
        DarkCyan,
        DarkGoldenrod,
        DarkGray,
        DarkGreen,
        DarkKhaki,
        DarkMagenta,
        DarkOliveGreen,
        DarkOrange,
        DarkOrchid,
        DarkRed,
        DarkSalmon,
        DarkSeaGreen,
        DarkSlateBlue,
        DarkSlateGray,
        DarkTurquoise,
        DarkViolet,
        DeepPink,
        DeepSkyBlue,
        DimGray,
        DodgerBlue,
        Firebrick,
        FloralWhite,
        ForestGreen,
        Fuchsia,
        Gainsboro,
        GhostWhite,
        Gold,
        Goldenrod,
        Gray,
        Green,
        GreenYellow,
        Honeydew,
        HotPink,
        IndianRed,
        Indigo,
        Ivory,
        Khaki,
        Lavender,
        LavenderBlush,
        LawnGreen,
        LemonChiffon,
        LightBlue,
        LightCoral,
        LightCyan,
        LightGoldenrodYellow,
        LightGray,
        LightGreen,
        LightPink,
        LightSalmon,
        LightSeaGreen,
        LightSkyBlue,
        LightSlateGray,
        LightSteelBlue,
        LightYellow,
        Lime,
        LimeGreen,
        Linen,
        Magenta,
        Maroon,
        MediumAquamarine,
        MediumBlue,
        MediumOrchid,
        MediumPurple,
        MediumSeaGreen,
        MediumSlateBlue,
        MediumSpringGreen,
        MediumTurquoise,
        MediumVioletRed,
        MidnightBlue,
        MintCream,
        MistyRose,
        Moccasin,
        NavajoWhite,
        Navy,
        OldLace,
        Olive,
        OliveDrab,
        Orange,
        OrangeRed,
        Orchid,
        PaleGoldenrod,
        PaleGreen,
        PaleTurquoise,
        PaleVioletRed,
        PapayaWhip,
        PeachPuff,
        Peru,
        Pink,
        Plum,
        PowderBlue,
        Purple,
        Red,
        RosyBrown,
        RoyalBlue,
        SaddleBrown,
        Salmon,
        SandyBrown,
        SeaGreen,
        SeaShell,
        Sienna,
        Silver,
        SkyBlue,
        SlateBlue,
        SlateGray,
        Snow,
        SpringGreen,
        SteelBlue,
        Tan,
        Teal,
        Thistle,
        Tomato,
        Turquoise,
        Violet,
        Wheat,
        White,
        WhiteSmoke,
        Yellow,
        YellowGreen,

        // "System" colors, Part 2
        ButtonFace,
        ButtonHighlight,
        ButtonShadow,
        GradientActiveCaption,
        GradientInactiveCaption,
        MenuBar,
        MenuHighlight,

        // "Web" colors, Part 2
        /// <summary>
        /// A system defined color representing the ARGB value <c>#663399</c>.
        /// </summary>
        RebeccaPurple,
    }
    public readonly struct Color : IEquatable<Color>
    {
        public static readonly Color Empty;

        private readonly string name;

        private readonly long value;

        private readonly short knownColor;

        private readonly short state;

        public static Color Transparent => new Color(KnownColor.Transparent);

        public static Color AliceBlue => new Color(KnownColor.AliceBlue);

        public static Color AntiqueWhite => new Color(KnownColor.AntiqueWhite);

        public static Color Aqua => new Color(KnownColor.Aqua);

        public static Color Aquamarine => new Color(KnownColor.Aquamarine);

        public static Color Azure => new Color(KnownColor.Azure);

        public static Color Beige => new Color(KnownColor.Beige);

        public static Color Bisque => new Color(KnownColor.Bisque);

        public static Color Black => new Color(KnownColor.Black);

        public static Color BlanchedAlmond => new Color(KnownColor.BlanchedAlmond);

        public static Color Blue => new Color(KnownColor.Blue);

        public static Color BlueViolet => new Color(KnownColor.BlueViolet);

        public static Color Brown => new Color(KnownColor.Brown);

        public static Color BurlyWood => new Color(KnownColor.BurlyWood);

        public static Color CadetBlue => new Color(KnownColor.CadetBlue);

        public static Color Chartreuse => new Color(KnownColor.Chartreuse);

        public static Color Chocolate => new Color(KnownColor.Chocolate);

        public static Color Coral => new Color(KnownColor.Coral);

        public static Color CornflowerBlue => new Color(KnownColor.CornflowerBlue);

        public static Color Cornsilk => new Color(KnownColor.Cornsilk);

        public static Color Crimson => new Color(KnownColor.Crimson);

        public static Color Cyan => new Color(KnownColor.Cyan);

        public static Color DarkBlue => new Color(KnownColor.DarkBlue);

        public static Color DarkCyan => new Color(KnownColor.DarkCyan);

        public static Color DarkGoldenrod => new Color(KnownColor.DarkGoldenrod);

        public static Color DarkGray => new Color(KnownColor.DarkGray);

        public static Color DarkGreen => new Color(KnownColor.DarkGreen);

        public static Color DarkKhaki => new Color(KnownColor.DarkKhaki);

        public static Color DarkMagenta => new Color(KnownColor.DarkMagenta);

        public static Color DarkOliveGreen => new Color(KnownColor.DarkOliveGreen);

        public static Color DarkOrange => new Color(KnownColor.DarkOrange);

        public static Color DarkOrchid => new Color(KnownColor.DarkOrchid);

        public static Color DarkRed => new Color(KnownColor.DarkRed);

        public static Color DarkSalmon => new Color(KnownColor.DarkSalmon);

        public static Color DarkSeaGreen => new Color(KnownColor.DarkSeaGreen);

        public static Color DarkSlateBlue => new Color(KnownColor.DarkSlateBlue);

        public static Color DarkSlateGray => new Color(KnownColor.DarkSlateGray);

        public static Color DarkTurquoise => new Color(KnownColor.DarkTurquoise);

        public static Color DarkViolet => new Color(KnownColor.DarkViolet);

        public static Color DeepPink => new Color(KnownColor.DeepPink);

        public static Color DeepSkyBlue => new Color(KnownColor.DeepSkyBlue);

        public static Color DimGray => new Color(KnownColor.DimGray);

        public static Color DodgerBlue => new Color(KnownColor.DodgerBlue);

        public static Color Firebrick => new Color(KnownColor.Firebrick);

        public static Color FloralWhite => new Color(KnownColor.FloralWhite);

        public static Color ForestGreen => new Color(KnownColor.ForestGreen);

        public static Color Fuchsia => new Color(KnownColor.Fuchsia);

        public static Color Gainsboro => new Color(KnownColor.Gainsboro);

        public static Color GhostWhite => new Color(KnownColor.GhostWhite);

        public static Color Gold => new Color(KnownColor.Gold);

        public static Color Goldenrod => new Color(KnownColor.Goldenrod);

        public static Color Gray => new Color(KnownColor.Gray);

        public static Color Green => new Color(KnownColor.Green);

        public static Color GreenYellow => new Color(KnownColor.GreenYellow);

        public static Color Honeydew => new Color(KnownColor.Honeydew);

        public static Color HotPink => new Color(KnownColor.HotPink);

        public static Color IndianRed => new Color(KnownColor.IndianRed);

        public static Color Indigo => new Color(KnownColor.Indigo);

        public static Color Ivory => new Color(KnownColor.Ivory);

        public static Color Khaki => new Color(KnownColor.Khaki);

        public static Color Lavender => new Color(KnownColor.Lavender);

        public static Color LavenderBlush => new Color(KnownColor.LavenderBlush);

        public static Color LawnGreen => new Color(KnownColor.LawnGreen);

        public static Color LemonChiffon => new Color(KnownColor.LemonChiffon);

        public static Color LightBlue => new Color(KnownColor.LightBlue);

        public static Color LightCoral => new Color(KnownColor.LightCoral);

        public static Color LightCyan => new Color(KnownColor.LightCyan);

        public static Color LightGoldenrodYellow => new Color(KnownColor.LightGoldenrodYellow);

        public static Color LightGreen => new Color(KnownColor.LightGreen);

        public static Color LightGray => new Color(KnownColor.LightGray);

        public static Color LightPink => new Color(KnownColor.LightPink);

        public static Color LightSalmon => new Color(KnownColor.LightSalmon);

        public static Color LightSeaGreen => new Color(KnownColor.LightSeaGreen);

        public static Color LightSkyBlue => new Color(KnownColor.LightSkyBlue);

        public static Color LightSlateGray => new Color(KnownColor.LightSlateGray);

        public static Color LightSteelBlue => new Color(KnownColor.LightSteelBlue);

        public static Color LightYellow => new Color(KnownColor.LightYellow);

        public static Color Lime => new Color(KnownColor.Lime);

        public static Color LimeGreen => new Color(KnownColor.LimeGreen);

        public static Color Linen => new Color(KnownColor.Linen);

        public static Color Magenta => new Color(KnownColor.Magenta);

        public static Color Maroon => new Color(KnownColor.Maroon);

        public static Color MediumAquamarine => new Color(KnownColor.MediumAquamarine);

        public static Color MediumBlue => new Color(KnownColor.MediumBlue);

        public static Color MediumOrchid => new Color(KnownColor.MediumOrchid);

        public static Color MediumPurple => new Color(KnownColor.MediumPurple);

        public static Color MediumSeaGreen => new Color(KnownColor.MediumSeaGreen);

        public static Color MediumSlateBlue => new Color(KnownColor.MediumSlateBlue);

        public static Color MediumSpringGreen => new Color(KnownColor.MediumSpringGreen);

        public static Color MediumTurquoise => new Color(KnownColor.MediumTurquoise);

        public static Color MediumVioletRed => new Color(KnownColor.MediumVioletRed);

        public static Color MidnightBlue => new Color(KnownColor.MidnightBlue);

        public static Color MintCream => new Color(KnownColor.MintCream);

        public static Color MistyRose => new Color(KnownColor.MistyRose);

        public static Color Moccasin => new Color(KnownColor.Moccasin);

        public static Color NavajoWhite => new Color(KnownColor.NavajoWhite);

        public static Color Navy => new Color(KnownColor.Navy);

        public static Color OldLace => new Color(KnownColor.OldLace);

        public static Color Olive => new Color(KnownColor.Olive);

        public static Color OliveDrab => new Color(KnownColor.OliveDrab);

        public static Color Orange => new Color(KnownColor.Orange);

        public static Color OrangeRed => new Color(KnownColor.OrangeRed);

        public static Color Orchid => new Color(KnownColor.Orchid);

        public static Color PaleGoldenrod => new Color(KnownColor.PaleGoldenrod);

        public static Color PaleGreen => new Color(KnownColor.PaleGreen);

        public static Color PaleTurquoise => new Color(KnownColor.PaleTurquoise);

        public static Color PaleVioletRed => new Color(KnownColor.PaleVioletRed);

        public static Color PapayaWhip => new Color(KnownColor.PapayaWhip);

        public static Color PeachPuff => new Color(KnownColor.PeachPuff);

        public static Color Peru => new Color(KnownColor.Peru);

        public static Color Pink => new Color(KnownColor.Pink);

        public static Color Plum => new Color(KnownColor.Plum);

        public static Color PowderBlue => new Color(KnownColor.PowderBlue);

        public static Color Purple => new Color(KnownColor.Purple);

        public static Color RebeccaPurple => new Color(KnownColor.RebeccaPurple);

        public static Color Red => new Color(KnownColor.Red);

        public static Color RosyBrown => new Color(KnownColor.RosyBrown);

        public static Color RoyalBlue => new Color(KnownColor.RoyalBlue);

        public static Color SaddleBrown => new Color(KnownColor.SaddleBrown);

        public static Color Salmon => new Color(KnownColor.Salmon);

        public static Color SandyBrown => new Color(KnownColor.SandyBrown);

        public static Color SeaGreen => new Color(KnownColor.SeaGreen);

        public static Color SeaShell => new Color(KnownColor.SeaShell);

        public static Color Sienna => new Color(KnownColor.Sienna);

        public static Color Silver => new Color(KnownColor.Silver);

        public static Color SkyBlue => new Color(KnownColor.SkyBlue);

        public static Color SlateBlue => new Color(KnownColor.SlateBlue);

        public static Color SlateGray => new Color(KnownColor.SlateGray);

        public static Color Snow => new Color(KnownColor.Snow);

        public static Color SpringGreen => new Color(KnownColor.SpringGreen);

        public static Color SteelBlue => new Color(KnownColor.SteelBlue);

        public static Color Tan => new Color(KnownColor.Tan);

        public static Color Teal => new Color(KnownColor.Teal);

        public static Color Thistle => new Color(KnownColor.Thistle);

        public static Color Tomato => new Color(KnownColor.Tomato);

        public static Color Turquoise => new Color(KnownColor.Turquoise);

        public static Color Violet => new Color(KnownColor.Violet);

        public static Color Wheat => new Color(KnownColor.Wheat);

        public static Color White => new Color(KnownColor.White);

        public static Color WhiteSmoke => new Color(KnownColor.WhiteSmoke);

        public static Color Yellow => new Color(KnownColor.Yellow);

        public static Color YellowGreen => new Color(KnownColor.YellowGreen);

        public byte R => (byte)(Value >> 16);

        public byte G => (byte)(Value >> 8);

        public byte B => (byte)Value;

        public byte A => (byte)(Value >> 24);

        public bool IsKnownColor => (state & 1) != 0;

        public bool IsEmpty => state == 0;

        public bool IsNamedColor
        {
            get
            {
                if ((state & 8) == 0)
                {
                    return IsKnownColor;
                }

                return true;
            }
        }

        public bool IsSystemColor
        {
            get
            {
                if (IsKnownColor)
                {
                    return IsKnownColorSystem((KnownColor)knownColor);
                }

                return false;
            }
        }

        private string NameAndARGBValue => $"{{Name = {Name}, ARGB = ({A}, {R}, {G}, {B})}}";

        public string Name
        {
            get
            {
                if ((state & 8) != 0)
                {
                    return name;
                }

                if (IsKnownColor)
                {
                    return KnownColorNames.KnownColorToName((KnownColor)knownColor);
                }

                return value.ToString("x");
            }
        }

        private long Value
        {
            get
            {
                if ((state & 2) != 0)
                {
                    return value;
                }

                if (IsKnownColor)
                {
                    return KnownColorTable.KnownColorToArgb((KnownColor)knownColor);
                }

                return 0L;
            }
        }

        internal Color(KnownColor knownColor)
        {
            value = 0L;
            state = 1;
            name = null;
            this.knownColor = (short)knownColor;
        }

        private Color(long value, short state, string name, KnownColor knownColor)
        {
            this.value = value;
            this.state = state;
            this.name = name;
            this.knownColor = (short)knownColor;
        }

        internal static bool IsKnownColorSystem(KnownColor knownColor)
        {
            return KnownColorTable.ColorKindTable[(int)knownColor] == 0;
        }

        private static void CheckByte(int value, string name)
        {
            if ((uint)value > 255u)
            {
                ThrowOutOfByteRange(value, name);
            }

            static void ThrowOutOfByteRange(int v, string n)
            {
                throw new ArgumentException();
            }
        }

        private static Color FromArgb(uint argb)
        {
            return new Color(argb, 2, null, (KnownColor)0);
        }

        public static Color FromArgb(int argb)
        {
            return FromArgb((uint)argb);
        }

        public static Color FromArgb(int alpha, int red, int green, int blue)
        {
            CheckByte(alpha, "alpha");
            CheckByte(red, "red");
            CheckByte(green, "green");
            CheckByte(blue, "blue");
            return FromArgb((uint)((alpha << 24) | (red << 16) | (green << 8) | blue));
        }

        public static Color FromArgb(int alpha, Color baseColor)
        {
            CheckByte(alpha, "alpha");
            return FromArgb((uint)((alpha << 24) | ((int)baseColor.Value & 0xFFFFFF)));
        }

        public static Color FromArgb(int red, int green, int blue)
        {
            return FromArgb(255, red, green, blue);
        }

        public static Color FromKnownColor(KnownColor color)
        {
            if (color > (KnownColor)0 && color <= KnownColor.RebeccaPurple)
            {
                return new Color(color);
            }

            return FromName(color.ToString());
        }

        public static Color FromName(string name)
        {
            if (ColorTable.TryGetNamedColor(name, out var result))
            {
                return result;
            }

            return new Color(0L, 8, name, (KnownColor)0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetRgbValues(out int r, out int g, out int b)
        {
            uint num = (uint)Value;
            r = (int)(num & 0xFF0000) >> 16;
            g = (int)(num & 0xFF00) >> 8;
            b = (int)(num & 0xFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MinMaxRgb(out int min, out int max, int r, int g, int b)
        {
            if (r > g)
            {
                max = r;
                min = g;
            }
            else
            {
                max = g;
                min = r;
            }

            if (b > max)
            {
                max = b;
            }
            else if (b < min)
            {
                min = b;
            }
        }

        public float GetBrightness()
        {
            GetRgbValues(out var r, out var g, out var b);
            MinMaxRgb(out var min, out var max, r, g, b);
            return (float)(max + min) / 510f;
        }

        public float GetHue()
        {
            GetRgbValues(out var r, out var g, out var b);
            if (r == g && g == b)
            {
                return 0f;
            }

            MinMaxRgb(out var min, out var max, r, g, b);
            float num = max - min;
            float num2 = ((r == max) ? ((float)(g - b) / num) : ((g != max) ? ((float)(r - g) / num + 4f) : ((float)(b - r) / num + 2f)));
            num2 *= 60f;
            if (num2 < 0f)
            {
                num2 += 360f;
            }

            return num2;
        }

        public float GetSaturation()
        {
            GetRgbValues(out var r, out var g, out var b);
            if (r == g && g == b)
            {
                return 0f;
            }

            MinMaxRgb(out var min, out var max, r, g, b);
            int num = max + min;
            if (num > 255)
            {
                num = 510 - max - min;
            }

            return (float)(max - min) / (float)num;
        }

        public int ToArgb()
        {
            return (int)Value;
        }

        public KnownColor ToKnownColor()
        {
            return (KnownColor)knownColor;
        }

        public override string ToString()
        {
            if (!IsNamedColor)
            {
                if ((state & 2) == 0)
                {
                    return "Color [Empty]";
                }

                return $"{"Color"} [A={A}, R={R}, G={G}, B={B}]";
            }

            return "Color [" + Name + "]";
        }

        public static bool operator ==(Color left, Color right)
        {
            if (left.value == right.value && left.state == right.state && left.knownColor == right.knownColor)
            {
                return left.name == right.name;
            }

            return false;
        }

        public static bool operator !=(Color left, Color right)
        {
            return !(left == right);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is Color other)
            {
                return Equals(other);
            }

            return false;
        }

        public bool Equals(Color other)
        {
            return this == other;
        }

        public override int GetHashCode()
        {
            if (name != null && !IsKnownColor)
            {
                return name.GetHashCode();
            }

            return HashCode.Combine(value.GetHashCode(), state.GetHashCode(), knownColor.GetHashCode());
        }
    }
    internal static class ColorTable
    {
        public static bool TryGetNamedColor(string name, out Color result)
        {
            result = default(Color);
            return false;
        }
        internal static bool IsKnownNamedColor(string name)
        {
            return false;
        }
    }
    internal static class KnownColorNames
    {
        private static readonly string[] s_colorNameTable = new string[]
       {
            // "System" colors, Part 1
            "ActiveBorder",
            "ActiveCaption",
            "ActiveCaptionText",
            "AppWorkspace",
            "Control",
            "ControlDark",
            "ControlDarkDark",
            "ControlLight",
            "ControlLightLight",
            "ControlText",
            "Desktop",
            "GrayText",
            "Highlight",
            "HighlightText",
            "HotTrack",
            "InactiveBorder",
            "InactiveCaption",
            "InactiveCaptionText",
            "Info",
            "InfoText",
            "Menu",
            "MenuText",
            "ScrollBar",
            "Window",
            "WindowFrame",
            "WindowText",

            // "Web" Colors, Part 1
            "Transparent",
            "AliceBlue",
            "AntiqueWhite",
            "Aqua",
            "Aquamarine",
            "Azure",
            "Beige",
            "Bisque",
            "Black",
            "BlanchedAlmond",
            "Blue",
            "BlueViolet",
            "Brown",
            "BurlyWood",
            "CadetBlue",
            "Chartreuse",
            "Chocolate",
            "Coral",
            "CornflowerBlue",
            "Cornsilk",
            "Crimson",
            "Cyan",
            "DarkBlue",
            "DarkCyan",
            "DarkGoldenrod",
            "DarkGray",
            "DarkGreen",
            "DarkKhaki",
            "DarkMagenta",
            "DarkOliveGreen",
            "DarkOrange",
            "DarkOrchid",
            "DarkRed",
            "DarkSalmon",
            "DarkSeaGreen",
            "DarkSlateBlue",
            "DarkSlateGray",
            "DarkTurquoise",
            "DarkViolet",
            "DeepPink",
            "DeepSkyBlue",
            "DimGray",
            "DodgerBlue",
            "Firebrick",
            "FloralWhite",
            "ForestGreen",
            "Fuchsia",
            "Gainsboro",
            "GhostWhite",
            "Gold",
            "Goldenrod",
            "Gray",
            "Green",
            "GreenYellow",
            "Honeydew",
            "HotPink",
            "IndianRed",
            "Indigo",
            "Ivory",
            "Khaki",
            "Lavender",
            "LavenderBlush",
            "LawnGreen",
            "LemonChiffon",
            "LightBlue",
            "LightCoral",
            "LightCyan",
            "LightGoldenrodYellow",
            "LightGray",
            "LightGreen",
            "LightPink",
            "LightSalmon",
            "LightSeaGreen",
            "LightSkyBlue",
            "LightSlateGray",
            "LightSteelBlue",
            "LightYellow",
            "Lime",
            "LimeGreen",
            "Linen",
            "Magenta",
            "Maroon",
            "MediumAquamarine",
            "MediumBlue",
            "MediumOrchid",
            "MediumPurple",
            "MediumSeaGreen",
            "MediumSlateBlue",
            "MediumSpringGreen",
            "MediumTurquoise",
            "MediumVioletRed",
            "MidnightBlue",
            "MintCream",
            "MistyRose",
            "Moccasin",
            "NavajoWhite",
            "Navy",
            "OldLace",
            "Olive",
            "OliveDrab",
            "Orange",
            "OrangeRed",
            "Orchid",
            "PaleGoldenrod",
            "PaleGreen",
            "PaleTurquoise",
            "PaleVioletRed",
            "PapayaWhip",
            "PeachPuff",
            "Peru",
            "Pink",
            "Plum",
            "PowderBlue",
            "Purple",
            "Red",
            "RosyBrown",
            "RoyalBlue",
            "SaddleBrown",
            "Salmon",
            "SandyBrown",
            "SeaGreen",
            "SeaShell",
            "Sienna",
            "Silver",
            "SkyBlue",
            "SlateBlue",
            "SlateGray",
            "Snow",
            "SpringGreen",
            "SteelBlue",
            "Tan",
            "Teal",
            "Thistle",
            "Tomato",
            "Turquoise",
            "Violet",
            "Wheat",
            "White",
            "WhiteSmoke",
            "Yellow",
            "YellowGreen",

            // "System" colors, Part 2
            "ButtonFace",
            "ButtonHighlight",
            "ButtonShadow",
            "GradientActiveCaption",
            "GradientInactiveCaption",
            "MenuBar",
            "MenuHighlight",

            // "Web" colors, Part 2
            "RebeccaPurple",
       };

        public static string KnownColorToName(KnownColor color)
        {
            return s_colorNameTable[unchecked((int)color) - 1];
        }
    }
    internal static class KnownColorTable
    {
        public const byte KnownColorKindSystem = 0;
        public const byte KnownColorKindWeb = 1;
        public const byte KnownColorKindUnknown = 2;

        public static ReadOnlySpan<uint> ColorValueTable =>
        [
             // "not a known color"
            0,
            // "System" colors, Part 1
            0xFFD4D0C8,     // ActiveBorder
            0xFF0054E3,     // ActiveCaption
            0xFFFFFFFF,     // ActiveCaptionText
            0xFF808080,     // AppWorkspace
            0xFFECE9D8,     // Control
            0xFFACA899,     // ControlDark
            0xFF716F64,     // ControlDarkDark
            0xFFF1EFE2,     // ControlLight
            0xFFFFFFFF,     // ControlLightLight
            0xFF000000,     // ControlText
            0xFF004E98,     // Desktop
            0xFFACA899,     // GrayText
            0xFF316AC5,     // Highlight
            0xFFFFFFFF,     // HighlightText
            0xFF000080,     // HotTrack
            0xFFD4D0C8,     // InactiveBorder
            0xFF7A96DF,     // InactiveCaption
            0xFFD8E4F8,     // InactiveCaptionText
            0xFFFFFFE1,     // Info
            0xFF000000,     // InfoText
            0xFFFFFFFF,     // Menu
            0xFF000000,     // MenuText
            0xFFD4D0C8,     // ScrollBar
            0xFFFFFFFF,     // Window
            0xFF000000,     // WindowFrame
            0xFF000000,     // WindowText

            // "Web" Colors, Part 1
            0x00FFFFFF,     // Transparent
            0xFFF0F8FF,     // AliceBlue
            0xFFFAEBD7,     // AntiqueWhite
            0xFF00FFFF,     // Aqua
            0xFF7FFFD4,     // Aquamarine
            0xFFF0FFFF,     // Azure
            0xFFF5F5DC,     // Beige
            0xFFFFE4C4,     // Bisque
            0xFF000000,     // Black
            0xFFFFEBCD,     // BlanchedAlmond
            0xFF0000FF,     // Blue
            0xFF8A2BE2,     // BlueViolet
            0xFFA52A2A,     // Brown
            0xFFDEB887,     // BurlyWood
            0xFF5F9EA0,     // CadetBlue
            0xFF7FFF00,     // Chartreuse
            0xFFD2691E,     // Chocolate
            0xFFFF7F50,     // Coral
            0xFF6495ED,     // CornflowerBlue
            0xFFFFF8DC,     // Cornsilk
            0xFFDC143C,     // Crimson
            0xFF00FFFF,     // Cyan
            0xFF00008B,     // DarkBlue
            0xFF008B8B,     // DarkCyan
            0xFFB8860B,     // DarkGoldenrod
            0xFFA9A9A9,     // DarkGray
            0xFF006400,     // DarkGreen
            0xFFBDB76B,     // DarkKhaki
            0xFF8B008B,     // DarkMagenta
            0xFF556B2F,     // DarkOliveGreen
            0xFFFF8C00,     // DarkOrange
            0xFF9932CC,     // DarkOrchid
            0xFF8B0000,     // DarkRed
            0xFFE9967A,     // DarkSalmon
            0xFF8FBC8F,     // DarkSeaGreen
            0xFF483D8B,     // DarkSlateBlue
            0xFF2F4F4F,     // DarkSlateGray
            0xFF00CED1,     // DarkTurquoise
            0xFF9400D3,     // DarkViolet
            0xFFFF1493,     // DeepPink
            0xFF00BFFF,     // DeepSkyBlue
            0xFF696969,     // DimGray
            0xFF1E90FF,     // DodgerBlue
            0xFFB22222,     // Firebrick
            0xFFFFFAF0,     // FloralWhite
            0xFF228B22,     // ForestGreen
            0xFFFF00FF,     // Fuchsia
            0xFFDCDCDC,     // Gainsboro
            0xFFF8F8FF,     // GhostWhite
            0xFFFFD700,     // Gold
            0xFFDAA520,     // Goldenrod
            0xFF808080,     // Gray
            0xFF008000,     // Green
            0xFFADFF2F,     // GreenYellow
            0xFFF0FFF0,     // Honeydew
            0xFFFF69B4,     // HotPink
            0xFFCD5C5C,     // IndianRed
            0xFF4B0082,     // Indigo
            0xFFFFFFF0,     // Ivory
            0xFFF0E68C,     // Khaki
            0xFFE6E6FA,     // Lavender
            0xFFFFF0F5,     // LavenderBlush
            0xFF7CFC00,     // LawnGreen
            0xFFFFFACD,     // LemonChiffon
            0xFFADD8E6,     // LightBlue
            0xFFF08080,     // LightCoral
            0xFFE0FFFF,     // LightCyan
            0xFFFAFAD2,     // LightGoldenrodYellow
            0xFFD3D3D3,     // LightGray
            0xFF90EE90,     // LightGreen
            0xFFFFB6C1,     // LightPink
            0xFFFFA07A,     // LightSalmon
            0xFF20B2AA,     // LightSeaGreen
            0xFF87CEFA,     // LightSkyBlue
            0xFF778899,     // LightSlateGray
            0xFFB0C4DE,     // LightSteelBlue
            0xFFFFFFE0,     // LightYellow
            0xFF00FF00,     // Lime
            0xFF32CD32,     // LimeGreen
            0xFFFAF0E6,     // Linen
            0xFFFF00FF,     // Magenta
            0xFF800000,     // Maroon
            0xFF66CDAA,     // MediumAquamarine
            0xFF0000CD,     // MediumBlue
            0xFFBA55D3,     // MediumOrchid
            0xFF9370DB,     // MediumPurple
            0xFF3CB371,     // MediumSeaGreen
            0xFF7B68EE,     // MediumSlateBlue
            0xFF00FA9A,     // MediumSpringGreen
            0xFF48D1CC,     // MediumTurquoise
            0xFFC71585,     // MediumVioletRed
            0xFF191970,     // MidnightBlue
            0xFFF5FFFA,     // MintCream
            0xFFFFE4E1,     // MistyRose
            0xFFFFE4B5,     // Moccasin
            0xFFFFDEAD,     // NavajoWhite
            0xFF000080,     // Navy
            0xFFFDF5E6,     // OldLace
            0xFF808000,     // Olive
            0xFF6B8E23,     // OliveDrab
            0xFFFFA500,     // Orange
            0xFFFF4500,     // OrangeRed
            0xFFDA70D6,     // Orchid
            0xFFEEE8AA,     // PaleGoldenrod
            0xFF98FB98,     // PaleGreen
            0xFFAFEEEE,     // PaleTurquoise
            0xFFDB7093,     // PaleVioletRed
            0xFFFFEFD5,     // PapayaWhip
            0xFFFFDAB9,     // PeachPuff
            0xFFCD853F,     // Peru
            0xFFFFC0CB,     // Pink
            0xFFDDA0DD,     // Plum
            0xFFB0E0E6,     // PowderBlue
            0xFF800080,     // Purple
            0xFFFF0000,     // Red
            0xFFBC8F8F,     // RosyBrown
            0xFF4169E1,     // RoyalBlue
            0xFF8B4513,     // SaddleBrown
            0xFFFA8072,     // Salmon
            0xFFF4A460,     // SandyBrown
            0xFF2E8B57,     // SeaGreen
            0xFFFFF5EE,     // SeaShell
            0xFFA0522D,     // Sienna
            0xFFC0C0C0,     // Silver
            0xFF87CEEB,     // SkyBlue
            0xFF6A5ACD,     // SlateBlue
            0xFF708090,     // SlateGray
            0xFFFFFAFA,     // Snow
            0xFF00FF7F,     // SpringGreen
            0xFF4682B4,     // SteelBlue
            0xFFD2B48C,     // Tan
            0xFF008080,     // Teal
            0xFFD8BFD8,     // Thistle
            0xFFFF6347,     // Tomato
            0xFF40E0D0,     // Turquoise
            0xFFEE82EE,     // Violet
            0xFFF5DEB3,     // Wheat
            0xFFFFFFFF,     // White
            0xFFF5F5F5,     // WhiteSmoke
            0xFFFFFF00,     // Yellow
            0xFF9ACD32,     // YellowGreen

            // "System" colors, Part 2
            0xFFF0F0F0,     // ButtonFace
            0xFFFFFFFF,     // ButtonHighlight
            0xFFA0A0A0,     // ButtonShadow
            0xFFB9D1EA,     // GradientActiveCaption
            0xFFD7E4F2,     // GradientInactiveCaption
            0xFFF0F0F0,     // MenuBar
            0xFF3399FF,     // MenuHighlight

            // "Web" colors, Part 2
            0xFF663399,     // RebeccaPurple
        ];

        public static ReadOnlySpan<byte> ColorKindTable =>
        [
            // "not a known color"
            KnownColorKindUnknown,

            // "System" colors, Part 1
            KnownColorKindSystem,       // ActiveBorder
            KnownColorKindSystem,       // ActiveCaption
            KnownColorKindSystem,       // ActiveCaptionText
            KnownColorKindSystem,       // AppWorkspace
            KnownColorKindSystem,       // Control
            KnownColorKindSystem,       // ControlDark
            KnownColorKindSystem,       // ControlDarkDark
            KnownColorKindSystem,       // ControlLight
            KnownColorKindSystem,       // ControlLightLight
            KnownColorKindSystem,       // ControlText
            KnownColorKindSystem,       // Desktop
            KnownColorKindSystem,       // GrayText
            KnownColorKindSystem,       // Highlight
            KnownColorKindSystem,       // HighlightText
            KnownColorKindSystem,       // HotTrack
            KnownColorKindSystem,       // InactiveBorder
            KnownColorKindSystem,       // InactiveCaption
            KnownColorKindSystem,       // InactiveCaptionText
            KnownColorKindSystem,       // Info
            KnownColorKindSystem,       // InfoText
            KnownColorKindSystem,       // Menu
            KnownColorKindSystem,       // MenuText
            KnownColorKindSystem,       // ScrollBar
            KnownColorKindSystem,       // Window
            KnownColorKindSystem,       // WindowFrame
            KnownColorKindSystem,       // WindowText

            // "Web" Colors, Part 1
            KnownColorKindWeb,      // Transparent
            KnownColorKindWeb,      // AliceBlue
            KnownColorKindWeb,      // AntiqueWhite
            KnownColorKindWeb,      // Aqua
            KnownColorKindWeb,      // Aquamarine
            KnownColorKindWeb,      // Azure
            KnownColorKindWeb,      // Beige
            KnownColorKindWeb,      // Bisque
            KnownColorKindWeb,      // Black
            KnownColorKindWeb,      // BlanchedAlmond
            KnownColorKindWeb,      // Blue
            KnownColorKindWeb,      // BlueViolet
            KnownColorKindWeb,      // Brown
            KnownColorKindWeb,      // BurlyWood
            KnownColorKindWeb,      // CadetBlue
            KnownColorKindWeb,      // Chartreuse
            KnownColorKindWeb,      // Chocolate
            KnownColorKindWeb,      // Coral
            KnownColorKindWeb,      // CornflowerBlue
            KnownColorKindWeb,      // Cornsilk
            KnownColorKindWeb,      // Crimson
            KnownColorKindWeb,      // Cyan
            KnownColorKindWeb,      // DarkBlue
            KnownColorKindWeb,      // DarkCyan
            KnownColorKindWeb,      // DarkGoldenrod
            KnownColorKindWeb,      // DarkGray
            KnownColorKindWeb,      // DarkGreen
            KnownColorKindWeb,      // DarkKhaki
            KnownColorKindWeb,      // DarkMagenta
            KnownColorKindWeb,      // DarkOliveGreen
            KnownColorKindWeb,      // DarkOrange
            KnownColorKindWeb,      // DarkOrchid
            KnownColorKindWeb,      // DarkRed
            KnownColorKindWeb,      // DarkSalmon
            KnownColorKindWeb,      // DarkSeaGreen
            KnownColorKindWeb,      // DarkSlateBlue
            KnownColorKindWeb,      // DarkSlateGray
            KnownColorKindWeb,      // DarkTurquoise
            KnownColorKindWeb,      // DarkViolet
            KnownColorKindWeb,      // DeepPink
            KnownColorKindWeb,      // DeepSkyBlue
            KnownColorKindWeb,      // DimGray
            KnownColorKindWeb,      // DodgerBlue
            KnownColorKindWeb,      // Firebrick
            KnownColorKindWeb,      // FloralWhite
            KnownColorKindWeb,      // ForestGreen
            KnownColorKindWeb,      // Fuchsia
            KnownColorKindWeb,      // Gainsboro
            KnownColorKindWeb,      // GhostWhite
            KnownColorKindWeb,      // Gold
            KnownColorKindWeb,      // Goldenrod
            KnownColorKindWeb,      // Gray
            KnownColorKindWeb,      // Green
            KnownColorKindWeb,      // GreenYellow
            KnownColorKindWeb,      // Honeydew
            KnownColorKindWeb,      // HotPink
            KnownColorKindWeb,      // IndianRed
            KnownColorKindWeb,      // Indigo
            KnownColorKindWeb,      // Ivory
            KnownColorKindWeb,      // Khaki
            KnownColorKindWeb,      // Lavender
            KnownColorKindWeb,      // LavenderBlush
            KnownColorKindWeb,      // LawnGreen
            KnownColorKindWeb,      // LemonChiffon
            KnownColorKindWeb,      // LightBlue
            KnownColorKindWeb,      // LightCoral
            KnownColorKindWeb,      // LightCyan
            KnownColorKindWeb,      // LightGoldenrodYellow
            KnownColorKindWeb,      // LightGray
            KnownColorKindWeb,      // LightGreen
            KnownColorKindWeb,      // LightPink
            KnownColorKindWeb,      // LightSalmon
            KnownColorKindWeb,      // LightSeaGreen
            KnownColorKindWeb,      // LightSkyBlue
            KnownColorKindWeb,      // LightSlateGray
            KnownColorKindWeb,      // LightSteelBlue
            KnownColorKindWeb,      // LightYellow
            KnownColorKindWeb,      // Lime
            KnownColorKindWeb,      // LimeGreen
            KnownColorKindWeb,      // Linen
            KnownColorKindWeb,      // Magenta
            KnownColorKindWeb,      // Maroon
            KnownColorKindWeb,      // MediumAquamarine
            KnownColorKindWeb,      // MediumBlue
            KnownColorKindWeb,      // MediumOrchid
            KnownColorKindWeb,      // MediumPurple
            KnownColorKindWeb,      // MediumSeaGreen
            KnownColorKindWeb,      // MediumSlateBlue
            KnownColorKindWeb,      // MediumSpringGreen
            KnownColorKindWeb,      // MediumTurquoise
            KnownColorKindWeb,      // MediumVioletRed
            KnownColorKindWeb,      // MidnightBlue
            KnownColorKindWeb,      // MintCream
            KnownColorKindWeb,      // MistyRose
            KnownColorKindWeb,      // Moccasin
            KnownColorKindWeb,      // NavajoWhite
            KnownColorKindWeb,      // Navy
            KnownColorKindWeb,      // OldLace
            KnownColorKindWeb,      // Olive
            KnownColorKindWeb,      // OliveDrab
            KnownColorKindWeb,      // Orange
            KnownColorKindWeb,      // OrangeRed
            KnownColorKindWeb,      // Orchid
            KnownColorKindWeb,      // PaleGoldenrod
            KnownColorKindWeb,      // PaleGreen
            KnownColorKindWeb,      // PaleTurquoise
            KnownColorKindWeb,      // PaleVioletRed
            KnownColorKindWeb,      // PapayaWhip
            KnownColorKindWeb,      // PeachPuff
            KnownColorKindWeb,      // Peru
            KnownColorKindWeb,      // Pink
            KnownColorKindWeb,      // Plum
            KnownColorKindWeb,      // PowderBlue
            KnownColorKindWeb,      // Purple
            KnownColorKindWeb,      // Red
            KnownColorKindWeb,      // RosyBrown
            KnownColorKindWeb,      // RoyalBlue
            KnownColorKindWeb,      // SaddleBrown
            KnownColorKindWeb,      // Salmon
            KnownColorKindWeb,      // SandyBrown
            KnownColorKindWeb,      // SeaGreen
            KnownColorKindWeb,      // SeaShell
            KnownColorKindWeb,      // Sienna
            KnownColorKindWeb,      // Silver
            KnownColorKindWeb,      // SkyBlue
            KnownColorKindWeb,      // SlateBlue
            KnownColorKindWeb,      // SlateGray
            KnownColorKindWeb,      // Snow
            KnownColorKindWeb,      // SpringGreen
            KnownColorKindWeb,      // SteelBlue
            KnownColorKindWeb,      // Tan
            KnownColorKindWeb,      // Teal
            KnownColorKindWeb,      // Thistle
            KnownColorKindWeb,      // Tomato
            KnownColorKindWeb,      // Turquoise
            KnownColorKindWeb,      // Violet
            KnownColorKindWeb,      // Wheat
            KnownColorKindWeb,      // White
            KnownColorKindWeb,      // WhiteSmoke
            KnownColorKindWeb,      // Yellow
            KnownColorKindWeb,      // YellowGreen

            // "System" colors, Part 1
            KnownColorKindSystem,       // ButtonFace
            KnownColorKindSystem,       // ButtonHighlight
            KnownColorKindSystem,       // ButtonShadow
            KnownColorKindSystem,       // GradientActiveCaption
            KnownColorKindSystem,       // GradientInactiveCaption
            KnownColorKindSystem,       // MenuBar
            KnownColorKindSystem,       // MenuHighlight

            // "Web" colors, Part 2
            KnownColorKindWeb,      // RebeccaPurple
        ];

        private static ReadOnlySpan<uint> AlternateSystemColors =>
        [
            0,          // To align with KnownColor.ActiveBorder = 1

                        // Existing   New
            0xFF464646, // FFB4B4B4 - FF464646: ActiveBorder - Dark gray
            0xFF3C5F78, // FF99B4D1 - FF3C5F78: ActiveCaption - Highlighted Text Background
            0xFFFFFFFF, // FF000000 - FFBEBEBE: ActiveCaptionText - White
            0xFF3C3C3C, // FFABABAB - FF3C3C3C: AppWorkspace - Panel Background
            0xFF202020, // FFF0F0F0 - FF373737: Control - Normal Panel/Windows Background
            0xFF4A4A4A, // FFA0A0A0 - FF464646: ControlDark - A lighter gray for dark mode
            0xFF5A5A5A, // FF696969 - FF5A5A5A: ControlDarkDark - An even lighter gray for dark mode
            0xFF2E2E2E, // FFE3E3E3 - FF2E2E2E: ControlLight - Unfocused Textbox Background
            0xFF1F1F1F, // FFFFFFFF - FF1F1F1F: ControlLightLight - Focused Textbox Background
            0xFFFFFFFF, // FF000000 - FFFFFFFF: ControlText - Control Forecolor and Text Color
            0xFF101010, // FF000000 - FF101010: Desktop - Black
            0xFF969696, // FF6D6D6D - FF969696: GrayText - Prompt Text Focused TextBox
            0xFF2864B4, // FF0078D7 - FF2864B4: Highlight - Highlighted Panel in DarkMode
            0xFF000000, // FFFFFFFF - FF000000: HighlightText - White
            0xFF2D5FAF, // FF0066CC - FF2D5FAF: HotTrack - Background of the ToggleSwitch
            0xFF3C3F41, // FFF4F7FC - FF3C3F41: InactiveBorder - Dark gray
            0xFF374B5A, // FFBFCBDD - FF374B5A: InactiveCaption - Highlighted Panel in DarkMode
            0xFFBEBEBE, // FF000000 - FFBEBEBE: InactiveCaptionText - Middle Dark Panel
            0xFF50503C, // FFFFFFE1 - FF50503C: Info - Link Label
            0xFFBEBEBE, // FF000000 - FFBEBEBE: InfoText - Prompt Text Color
            0xFF373737, // FFF0F0F0 - FF373737: Menu - Normal Menu Background
            0xFFF0F0F0, // FF000000 - FFF0F0F0: MenuText - White
            0xFF505050, // FFC8C8C8 - FF505050: ScrollBar - Scrollbars and Scrollbar Arrows
            0xFF323232, // FFFFFFFF - FF323232: Window - Window Background
            0xFF282828, // FF646464 - FF282828: WindowFrame - White
            0xFFF0F0F0, // FF000000 - FFF0F0F0: WindowText - White
            0xFF202020, // FFF0F0F0 - FF373737: ButtonFace - Same as Window Background
            0xFF101010, // FFFFFFFF - FF101010: ButtonHighlight - White
            0xFF464646, // FFA0A0A0 - FF464646: ButtonShadow - Same as Scrollbar Elements
            0XFF416482, // FFB9D1EA - FF416482: GradientActiveCaption - Same as Highlighted Text Background
            0xFF557396, // FFD7E4F2 - FF557396: GradientInactiveCaption - Same as Highlighted Panel in DarkMode
            0xFF373737, // FFF0F0F0 - FF373737: MenuBar - Same as Normal Menu Background
            0xFF2A80D2  // FF3399FF - FF2A80D2: MenuHighlight - Same as Highlighted Menu Background
        ];

        internal static Color ArgbToKnownColor(uint argb)
        {
            ReadOnlySpan<uint> colorValueTable = ColorValueTable;
            for (int index = 1; index < colorValueTable.Length; ++index)
            {
                if (ColorKindTable[index] == KnownColorKindWeb && colorValueTable[index] == argb)
                {
                    return Color.FromKnownColor((KnownColor)index);
                }
            }

            // Not a known color
            return Color.FromArgb((int)argb);
        }

        public static uint KnownColorToArgb(KnownColor color)
        {
            return ColorKindTable[(int)color] == KnownColorKindSystem
                 ? GetSystemColorArgb(color)
                 : ColorValueTable[(int)color];
        }

        private static uint GetAlternateSystemColorArgb(KnownColor color)
        {
            // Shift the original (split) index to fit the alternate color map.
            int index = color <= KnownColor.WindowText
                ? (int)color
                : (int)color - (int)KnownColor.ButtonFace + (int)KnownColor.WindowText + 1;

            return AlternateSystemColors[index];
        }

        public static uint GetSystemColorArgb(KnownColor color)
        {
            return ColorValueTable[(int)color];
        }
    }
    /// <summary>
    /// Represents the size of a rectangular region with an ordered pair of width and height.
    /// </summary>
    public struct Size
    {
        public static readonly Size Empty;

        private int width; // Do not rename
        private int height; // Do not rename

        public Size(Point pt)
        {
            width = pt.X;
            height = pt.Y;
        }

        public Size(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        public static Size operator +(Size sz1, Size sz2) => Add(sz1, sz2);

        public static Size operator -(Size sz1, Size sz2) => Subtract(sz1, sz2);

        public static explicit operator Point(Size size) => new Point(size.Width, size.Height);

        public readonly bool IsEmpty => width == 0 && height == 0;

        public int Width
        {
            readonly get => width;
            set => width = value;
        }

        public int Height
        {
            readonly get => height;
            set => height = value;
        }

        public static Size Add(Size sz1, Size sz2) =>
            new Size(unchecked(sz1.Width + sz2.Width), unchecked(sz1.Height + sz2.Height));

        public static Size Subtract(Size sz1, Size sz2) =>
            new Size(unchecked(sz1.Width - sz2.Width), unchecked(sz1.Height - sz2.Height));

        /// <summary>
        /// Converts a SizeF to a Size by performing a truncate operation on all the coordinates.
        /// </summary>
        public static Size Truncate(SizeF value) => new Size(unchecked((int)value.Width), unchecked((int)value.Height));

        /// <summary>
        /// Converts a SizeF to a Size by performing a round operation on all the coordinates.
        /// </summary>
        public static Size Round(SizeF value) =>
            new Size(unchecked((int)Math.Round(value.Width)), unchecked((int)Math.Round(value.Height)));

        public override readonly string ToString() => $"{{Width={width}, Height={height}}}";

        private static Size Multiply(Size size, int multiplier) =>
            new Size(unchecked(size.width * multiplier), unchecked(size.height * multiplier));

        private static SizeF Multiply(Size size, float multiplier) =>
            new SizeF(size.width * multiplier, size.height * multiplier);
    }
    /// <summary>
    /// Represents an ordered pair of x and y coordinates that define a point in a two-dimensional plane.
    /// </summary>
    public struct Point
    {
        public static readonly Point Empty;

        private int x; // Do not rename
        private int y; // Do not rename

        public Point(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
        public Point(Size sz)
        {
            x = sz.Width;
            y = sz.Height;
        }
        /// <summary>
        /// Initializes a new instance of the Point class using coordinates specified by an integer value.
        /// </summary>
        public Point(int dw)
        {
            x = LowInt16(dw);
            y = HighInt16(dw);
        }

        public readonly bool IsEmpty => x == 0 && y == 0;

        public int X
        {
            readonly get => x;
            set => x = value;
        }

        public int Y
        {
            readonly get => y;
            set => y = value;
        }

        public static explicit operator Size(Point p) => new Size(p.X, p.Y);

        public static Point operator +(Point pt, Size sz) => Add(pt, sz);

        public static Point operator -(Point pt, Size sz) => Subtract(pt, sz);

        public static bool operator ==(Point left, Point right) => left.X == right.X && left.Y == right.Y;

        public static bool operator !=(Point left, Point right) => !(left == right);

        public static Point Add(Point pt, Size sz) => new Point(unchecked(pt.X + sz.Width), unchecked(pt.Y + sz.Height));

        public static Point Subtract(Point pt, Size sz) => new Point(unchecked(pt.X - sz.Width), unchecked(pt.Y - sz.Height));

        public static Point Truncate(PointF value) => new Point(unchecked((int)value.X), unchecked((int)value.Y));

        public static Point Round(PointF value) => new Point(unchecked((int)Math.Round(value.X)), unchecked((int)Math.Round(value.Y)));

        public void Offset(int dx, int dy)
        {
            unchecked
            {
                X += dx;
                Y += dy;
            }
        }

        public void Offset(Point p) => Offset(p.X, p.Y);

        public override readonly string ToString() => $"{{X={X},Y={Y}}}";

        private static short HighInt16(int n) => unchecked((short)((n >> 16) & 0xffff));

        private static short LowInt16(int n) => unchecked((short)(n & 0xffff));
    }
    /// <summary>
    /// Represents the size of a rectangular region with an ordered pair of width and height.
    /// </summary>
    public struct SizeF
    {
        public static readonly SizeF Empty;
        private float width; // Do not rename
        private float height; // Do not rename

        public SizeF(SizeF size)
        {
            width = size.width;
            height = size.height;
        }

        public SizeF(PointF pt)
        {
            width = pt.X;
            height = pt.Y;
        }
        public SizeF(System.Numerics.Vector2 vector)
        {
            width = vector.X;
            height = vector.Y;
        }
        public System.Numerics.Vector2 ToVector2() => new System.Numerics.Vector2(width, height);
        public SizeF(float width, float height)
        {
            this.width = width;
            this.height = height;
        }

        public static explicit operator System.Numerics.Vector2(SizeF size) => size.ToVector2();

        public static explicit operator SizeF(System.Numerics.Vector2 vector) => new SizeF(vector);



        public readonly bool IsEmpty => width == 0 && height == 0;

        public float Width
        {
            readonly get => width;
            set => width = value;
        }

        public float Height
        {
            readonly get => height;
            set => height = value;
        }
    }
    /// <summary>
    /// Represents an ordered pair of x and y coordinates that define a point in a two-dimensional plane.
    /// </summary>
    public struct PointF
    {
        public static readonly PointF Empty;
        private float x; // Do not rename
        private float y; // Do not rename

        public PointF(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public PointF(System.Numerics.Vector2 vector)
        {
            x = vector.X;
            y = vector.Y;
        }

        public System.Numerics.Vector2 ToVector2() => new System.Numerics.Vector2(x, y);
        public readonly bool IsEmpty => x == 0f && y == 0f;

        public float X
        {
            readonly get => x;
            set => x = value;
        }
        public float Y
        {
            readonly get => y;
            set => y = value;
        }

        public static explicit operator System.Numerics.Vector2(PointF point) => point.ToVector2();
        public static explicit operator PointF(System.Numerics.Vector2 vector) => new PointF(vector);

    }
}