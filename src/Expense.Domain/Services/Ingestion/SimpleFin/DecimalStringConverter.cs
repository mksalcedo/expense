using System.Text.Json;
using System.Text.Json.Serialization;

namespace Expense.Domain.Services.Ingestion.SimpleFin;

/// <summary>
/// SimpleFin represents monetary amounts as JSON strings (avoiding float precision
/// loss), e.g. "balance": "6463.02" - this converts those to decimal on read.
/// </summary>
public class DecimalStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? decimal.Parse(reader.GetString()!)
            : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
