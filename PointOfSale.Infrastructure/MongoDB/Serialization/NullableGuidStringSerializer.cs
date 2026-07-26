using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace PointOfSale.Infrastructure.MongoDB.Serialization;

public sealed class NullableGuidStringSerializer : SerializerBase<Guid?>
{
    public override Guid? Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        return context.Reader.GetCurrentBsonType() switch
        {
            BsonType.Null => ReadNull(context),
            BsonType.String => ParseGuid(context.Reader.ReadString()),
            BsonType.Binary => context.Reader.ReadBinaryData().ToGuid(),
            _ => throw new BsonSerializationException(
                $"Cannot deserialize a nullable Guid from BSON type {context.Reader.GetCurrentBsonType()}.")
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Guid? value)
    {
        if (!value.HasValue)
        {
            context.Writer.WriteNull();
            return;
        }

        context.Writer.WriteString(value.Value.ToString());
    }

    private static Guid? ReadNull(BsonDeserializationContext context)
    {
        context.Reader.ReadNull();
        return null;
    }

    private static Guid? ParseGuid(string value)
    {
        if (Guid.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new BsonSerializationException($"'{value}' is not a valid Guid value.");
    }
}
