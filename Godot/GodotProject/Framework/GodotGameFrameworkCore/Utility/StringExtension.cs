using Godot;
using System;
using System.Text;
namespace GodotGameFramework.Extensions
{
    public static class StringExtension
    {
        public static string ReadLine(this string rawString, ref int position)
        {
            if (position < 0)
            {
                return null;
            }

            int length = rawString.Length;
            int offset = position;
            while (offset < length)
            {
                char ch = rawString[offset];
                switch (ch)
                {
                    case '\r':
                    case '\n':
                        if (offset > position)
                        {
                            string line = rawString.Substring(position, offset - position);
                            position = offset + 1;
                            if ((ch == '\r') && (position < length) && (rawString[position] == '\n'))
                            {
                                position++;
                            }

                            return line;
                        }

                        offset++;
                        position++;
                        break;

                    default:
                        offset++;
                        break;
                }
            }

            if (offset > position)
            {
                string line = rawString.Substring(position, offset - position);
                position = offset;
                return line;
            }

            return null;
        }
        public static string ColorString(this string str, string color)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("<color=");
            builder.Append(color);
            builder.Append(">");
            builder.Append(str);
            builder.Append("</color>");
            return builder.ToString();
        }
        public static string ColorString(this string str, Color color)
        {
            return str.ColorString(color.ToHtml());
        }
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1}MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2}GB";
        }
    }
}

