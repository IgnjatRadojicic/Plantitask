namespace Plantitask.Core.SignalR
{
    public static class HubEvents
    {
        public const string TaskMoved = "TaskMoved";
        public const string TaskCreated = "TaskCreated";
        public const string TaskDeleted = "TaskDeleted";
        public const string TaskUpdated = "TaskUpdated";

        public const string ReceiveNotification = "ReceiveNotification";

        public const string TreeUpdated = "TreeUpdated";
        public const string TreeAdded = "TreeAdded";
    }
}
