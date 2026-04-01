namespace LMMs.Api.Interfaces
{
    public interface IAgentTool
    {
        string Name { get; }
        Delegate GetFunction();
    }
}
