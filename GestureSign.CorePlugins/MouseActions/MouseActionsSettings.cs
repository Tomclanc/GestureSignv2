using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GestureSign.Common.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GestureSign.CorePlugins.MouseActions
{
    public class MouseActionDescription
    {
        static MouseActionDescription()
        {
            DescriptionDict = new Dictionary<MouseActions, string>(8);
            ButtonDescription = new Dictionary<MouseActions, string>(5);
            foreach (var button in new MouseActions[5] { MouseActions.LeftButton, MouseActions.MiddleButton, MouseActions.RightButton, MouseActions.XButton1, MouseActions.XButton2 })
            {
                ButtonDescription.Add(button, LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Buttons." + button));
            }
            foreach (var mouseAction in new List<MouseActions>() { MouseActions.Click, MouseActions.DoubleClick, MouseActions.Down, MouseActions.Up, MouseActions.VerticalScroll, MouseActions.HorizontalScroll, MouseActions.MoveMouseTo, MouseActions.MoveMouseBy })
            {
                DescriptionDict.Add(mouseAction,
                    LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.MouseActions." + mouseAction));
            }
        }

        public static Dictionary<MouseActions, string> DescriptionDict { get; private set; }
        public static Dictionary<MouseActions, string> ButtonDescription { get; private set; }
    }

    public class ClickPositionDescription
    {
        static ClickPositionDescription()
        {
            DescriptionDict = new Dictionary<ClickPositions, String>(6);
            RelativePositionDict = new Dictionary<ClickPositions, String>(5);

            DescriptionDict.Add(ClickPositions.Custom, LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.ClickPositions." + ClickPositions.Custom));

            foreach (ClickPositions position in new ClickPositions[5] { ClickPositions.Current, ClickPositions.FirstDown, ClickPositions.FirstUp, ClickPositions.LastDown, ClickPositions.LastUp })
            {
                DescriptionDict.Add(position,
                    LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.ClickPositions." + position));
                RelativePositionDict.Add(position,
                    LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.ClickPositions." + position));
            }
        }
        public static Dictionary<ClickPositions, String> DescriptionDict { get; private set; }
        public static Dictionary<ClickPositions, string> RelativePositionDict { get; private set; }
    }

    public class LegacyMouseActionsSettings
    {
        public LegacyMouseActions MouseAction { get; set; }
        public LegacyClickPositions ClickPosition { get; set; }
        [JsonConverter(typeof(MousePointJsonConverter))]
        public Point MovePoint { get; set; }
        public int ScrollAmount { get; set; }
    }

    public class MouseActionsSettings
    {
        public MouseActions MouseAction { get; set; }
        public ClickPositions ActionLocation { get; set; }
        [JsonConverter(typeof(MousePointJsonConverter))]
        public Point MovePoint { get; set; }
        public int ScrollAmount { get; set; }
        public int WaitMilliseconds { get; set; }
        public int MoveDurationMilliseconds { get; set; }
    }

    /// <summary>
    /// Keeps mouse-action settings compatible with both the legacy Json.NET
    /// Point string ("0, 0") and the WinUI object ({"X":0,"Y":0}).
    /// </summary>
    public sealed class MousePointJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Point);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartObject)
            {
                var point = JObject.Load(reader);
                return new Point(point.Value<int?>("X") ?? 0, point.Value<int?>("Y") ?? 0);
            }

            if (reader.TokenType == JsonToken.String)
            {
                var text = reader.Value as string;
                var converter = TypeDescriptor.GetConverter(typeof(Point));
                if (!string.IsNullOrWhiteSpace(text) && converter.CanConvertFrom(typeof(string)))
                    return (Point)converter.ConvertFromInvariantString(text);
            }

            if (reader.TokenType == JsonToken.Null)
                return Point.Empty;

            throw new JsonSerializationException($"Unsupported mouse point JSON token: {reader.TokenType}.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var point = value is Point typedPoint ? typedPoint : Point.Empty;
            writer.WriteStartObject();
            writer.WritePropertyName("X");
            writer.WriteValue(point.X);
            writer.WritePropertyName("Y");
            writer.WriteValue(point.Y);
            writer.WriteEndObject();
        }
    }
}
