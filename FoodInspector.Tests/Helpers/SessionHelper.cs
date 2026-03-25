using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FoodInspector.Tests.Helpers;

public static class SessionHelper
{
    /// <summary>
    /// Creates an <see cref="DefaultHttpContext"/> with a working in-memory session
    /// and assigns it to the controller.
    /// </summary>
    public static void SetupSession(Controller controller, int? userId = null, string? userName = null, int? userAge = null)
    {
        var httpContext = new DefaultHttpContext
        {
            Session = new TestSession()
        };

        if (userId.HasValue)
            httpContext.Session.SetInt32("UserId", userId.Value);
        if (userName is not null)
            httpContext.Session.SetString("UserName", userName);
        if (userAge.HasValue)
            httpContext.Session.SetInt32("UserAge", userAge.Value);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }
}

/// <summary>
/// Minimal in-memory ISession implementation for unit testing.
/// </summary>
public class TestSession : ISession
{
    private readonly Dictionary<string, byte[]> _store = new();

    public string Id => Guid.NewGuid().ToString();
    public bool IsAvailable => true;
    public IEnumerable<string> Keys => _store.Keys;

    public void Clear() => _store.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => _store.Remove(key);
    public void Set(string key, byte[] value) => _store[key] = value;

    public bool TryGetValue(string key, out byte[] value)
    {
        return _store.TryGetValue(key, out value!);
    }
}
