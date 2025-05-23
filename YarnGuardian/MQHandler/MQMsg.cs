using System.Text.Json;
using System.Text.Json.Serialization;

namespace YarnGuardian.MQHandler
{
    public static class MQMsg
    {
        public static IMQMsg Success<T>(T content = default(T))
        {
            return new MQMsg<T>().Success(content);
        }

        public static IMQMsg<T> Success<T>(IMQMsg<T> source, T content = default(T))
        {
            var msg = new MQMsg<T>()
            {
                ClientId = source.ClientId,
                Module = source.Module,
                Service = source.Service
            };
            return msg.Success(content);
        }

        public static IMQMsg Success()
        {
            return Success<string>();
        }

        public static IMQMsg Failed<T>(string error = null)
        {
            return new MQMsg<T>().Failed(error ?? "failed");
        }

        public static IMQMsg<T> Failed<T>(IMQMsg<T> source, string error = null)
        {
            var msg = new MQMsg<T>()
            {
                ClientId = source.ClientId,
                Module = source.Module,
                Service = source.Service
            };
            return msg.Failed(error ?? "failed");
        }

        public static IMQMsg Failed(string error = null)
        {
            return Failed<string>(error);
        }

        public static IMQMsg Result<T>(bool success)
        {
            return success ? Success<T>() : Failed<T>();
        }

        public static IMQMsg<T> Result<T>(IMQMsg<T> source, bool success)
        {
            return success ? Success<T>(source) : Failed<T>(source);
        }

        public static IMQMsg Result(bool success)
        {
            return success ? Success() : Failed();
        }

        public static MQMsg<T> Parse<T>(IMQMsg<Object> source)
        {
            var msg = new MQMsg<T>()
            {
                ClientId = source.ClientId,
                Module = source.Module,
                Service = source.Service
            };
            msg.Content = JsonSerializer.Deserialize<T>(source.Content.ToString());
            return msg;
        }
    }

    public class MQMsg<T> : IMQMsg<T>
    {
        public uint ClientId { get; set; }

        public string Module { get; set; } = string.Empty;

        public string Service { get; set; } = string.Empty;

        [JsonIgnore]
        public bool Successful { get; private set; }

        public string Msg { get; private set; } = string.Empty;

        public int Code => Successful ? 1 : 0;

        public T Content { get; set; }

        public MQMsg<T> Success(T content = default, string msg = "success")
        {
            Successful = true;
            Content = content;
            Msg = msg;
            return this;
        }

        public MQMsg<T> Failed(string msg = "failed")
        {
            Successful = false;
            Msg = msg;
            return this;
        }
    }
}
