// ================================================================
//  DataDisplayJson.cs  —  the serializer options a `.cdd` is read and
//  written with
//
//  RND-4. `DataDisplayViewModel.JsonOpts` was the only copy, and a CLI
//  that made its own would read the file with a DIFFERENT enum
//  convention — every PlotType, FreqUnit, MatrixType and ContourColorMap
//  in the document is written as a NAME, and without the converter they
//  all fail to bind and fall back to their defaults. That is a display
//  that opens, draws, and is wrong: a Smith plot rendered as a Rect one.
//
//  So the options live here and the view model forwards to them.
// ================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CircuitRF.Render.DataDisplay;

public static class DataDisplayJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() },
    };
}
