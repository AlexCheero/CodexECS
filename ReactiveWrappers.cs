namespace CodexECS
{
    public struct AddReact<T> : ITag {}

    public struct RemoveReact<T> : IComponent
    {
        public T removingComponent;
    }
}