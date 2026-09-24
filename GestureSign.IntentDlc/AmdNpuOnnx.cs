using System; using System.IO; using System.Linq; using GestureSign.IntentLearning;
using System.Text;

namespace GestureSign.IntentDlc;

/// <summary>Exports the normalized linear core as ONNX opset 17 Conv.
/// Field numbers follow https://github.com/onnx/onnx/blob/main/onnx/onnx.proto .
/// Normalization and sigmoid are performed on CPU; convolution executes on NPU.</summary>
internal static class AmdNpuOnnx
{
    public static byte[] Export(IntentModel model)
    {
        model.Validate();
        var weights = model.Weights;
        float bias = model.Bias;
        var graph = new Proto();
        graph.Message(1, Node("Conv", ["features", "weights", "bias"], "score"));

        graph.String(2, "GestureSignLocalIntent");
        graph.Message(5, Tensor("weights", [16, weights.Length, 1, 1], Enumerable.Range(0,16).SelectMany(_ => weights).ToArray()));
        graph.Message(5, Tensor("bias", [16], Enumerable.Repeat(bias, 16).ToArray()));
        graph.Message(11, Value("features", [1, weights.Length, 1, 1]));
        graph.Message(12, Value("score", [1, 16, 1, 1]));
        var result = new Proto(); result.Int(1, 7); result.String(2, "GestureSign.IntentDlc"); result.Message(7, graph);
        var opset = new Proto(); opset.Int(2, 17); result.Message(8, opset);
        return result.Bytes();
    }
    private static Proto Node(string op, string[] inputs, string output)
    {
        var p = new Proto(); foreach (var input in inputs) p.String(1, input); p.String(2, output); p.String(3, output + "_node"); p.String(4, op); return p;
    }
    private static Proto Tensor(string name, int[] dims, float[] data)
    {
        var p = new Proto(); foreach (var d in dims) p.Int(1, d); p.Int(2, 1); p.String(8, name);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var v in data) writer.Write(v); p.Data(9, stream.ToArray()); return p;
    }
    private static Proto Value(string name, int[] dims)
    {
        var shape = new Proto(); foreach (var dim in dims) { var d = new Proto(); d.Int(1, dim); shape.Message(1, d); }
        var tensor = new Proto(); tensor.Int(1, 1); tensor.Message(2, shape);
        var type = new Proto(); type.Message(1, tensor);
        var value = new Proto(); value.String(1, name); value.Message(2, type); return value;
    }
    private sealed class Proto
    {
        private readonly MemoryStream _stream = new();
        private void Varint(ulong value) { while (value > 127) { _stream.WriteByte((byte)((value & 127) | 128)); value >>= 7; } _stream.WriteByte((byte)value); }
        public void Int(int field, int value) { Varint((ulong)(field << 3)); Varint((ulong)value); }
        public void Data(int field, byte[] data) { Varint((ulong)((field << 3) | 2)); Varint((ulong)data.Length); _stream.Write(data); }
        public void String(int field, string value) => Data(field, Encoding.UTF8.GetBytes(value));
        public void Message(int field, Proto value) => Data(field, value.Bytes());
        public byte[] Bytes() => _stream.ToArray();
    }
}
