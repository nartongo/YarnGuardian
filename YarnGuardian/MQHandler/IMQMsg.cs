using System.Text.Json.Serialization;

namespace YarnGuardian.MQHandler
{
    public interface IMQMsg
    {
        uint ClientId { get; }

        string Module { get; }

        string Service { get; }

        [JsonIgnore]
        bool Successful { get; }

        string Msg { get; }
    }

    public interface IMQMsg<T> : IMQMsg
    {
        T Content { get; }
    }
}