namespace GestureSign.WinUI;

// Display captions are localized, but LegacyDataStore expects these fixed tokens.
internal static class GestureTemplateDirection
{
    public static string FromIndex(int index) => index switch
    {
        1 => "向左",
        2 => "向上",
        3 => "向下",
        4 => "左上",
        5 => "右上",
        6 => "左下",
        7 => "右下",
        _ => "向右"
    };
}