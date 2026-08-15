namespace KUKULCAN.SharedKernel.Database.Client.Client;

public sealed class ConsoleCurrentUser
{
    public bool IsAuthenticated { get; private set; } = true;
    public string UserName { get; private set; } = "demo-user";

    public void SetUser(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        IsAuthenticated = true;
        UserName = userName;
    }

    public void SetUnauthenticated()
    {
        IsAuthenticated = false;
        UserName = string.Empty;
    }
}

