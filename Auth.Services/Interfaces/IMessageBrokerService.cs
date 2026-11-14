namespace Auth.Services.Interfaces
{
    public interface IMessageBrokerService
    {
        /// <summary>False for the no-op broker, so callers can deliver directly instead of dropping work.</summary>
        bool DeliversMessages { get; }

        void PublishEmailMessage(string queueName, object message);
        void StartConsuming(string queueName, Action<string> callback);
    }
}
