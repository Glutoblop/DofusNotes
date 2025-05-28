using System;
using System.Globalization;
using Newtonsoft.Json;

public class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private const string Format = "yyyy-MM-dd";

    public override void WriteJson(JsonWriter writer, DateOnly value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }

    public override DateOnly ReadJson(JsonReader reader, Type objectType, DateOnly existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var dateStr = reader.Value?.ToString();
        return DateOnly.ParseExact(dateStr!, Format, CultureInfo.InvariantCulture);
    }
}
