using System.Text;

namespace GestureSign.IntentLearning;

/// <summary>Exports the fitted linear classifier as ONNX opset 13 (Gemm + Sigmoid).
/// Field numbers follow https://github.com/onnx/onnx/blob/main/onnx/onnx.proto .
/// Normalization is folded into weights, so hardware and CPU receive identical raw features.</summary>
public static class IntentOnnx
{
    public static byte[] Export(IntentModel model)
    {
        model.Validate();
        var weights = model.Weights.Select((w, i) => w / model.Scale[i]).ToArray();
        float bias = model.Bias - weights.Select((w, i) => w * model.Mean[i]).Sum();
        var graph = new Proto();
        graph.Message(1, Node("Gemm", ["features", "weights", "bias"], "logit"));
        graph.Message(1, Node("Sigmoid", ["logit"], "score"));
        graph.String(2, "GestureSignLocalIntent");
        graph.Message(5, Tensor("weights", [weights.Length, 1], weights));
        graph.Message(5, Tensor("bias", [1], [bias]));
        graph.Message(11, Value("features", [1, weights.Length]));
        graph.Message(12, Value("score", [1, 1]));
        var result = new Proto(); result.Int(1, 7); result.String(2, "GestureSign.IntentDlc"); result.Message(7, graph);
        var opset = new Proto(); opset.Int(2, 13); result.Message(8, opset);
        return result.Bytes();
    }
    private static Proto Node(string op, string[] inputs, string output)
    {
        var p = new Proto(); foreach (var input in inputs) p.String(1, input); p.String(2, output); p.String(4, op); return p;
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
