namespace Agent.Application.Search;
public interface IWebSearchProvider { Task<string> SearchAsync(string query, CancellationToken cancellationToken); }
