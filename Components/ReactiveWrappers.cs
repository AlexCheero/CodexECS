namespace CodexECS
{
    public struct AddReact<T> : IComponent {}

    public struct RemoveReact<T> : IComponent
    {
        public T removedComponent;
    }

    public struct MatchReact : IComponent {}
}