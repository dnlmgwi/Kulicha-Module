namespace Kulicha.Services {
using SpacetimeDB;
using SpacetimeDB.Types;

public class ReducerAsync {
    private readonly ILogger<SpacetimeDbService> _logger;
    private DbConnection? _conn;
    private CancellationTokenSource? _cts;
    private Task? _processTask;
    private volatile bool _isConnected;
    private string? _connectionError;

    // --- Events for Blazor components ---
    private event Action? OnConnect;
    private event Action? OnDisconnect;
    public event Action<Identity>? OnIdentityReceived;
    public event Action<User>? OnProfileReceived;
    public event Action<string, string>? OnErrorReceived; // (type, message)
    public event Action<string>? OnRegisterSuccess;
    public event Action<string>? OnVerifySuccess;
    public event Action<string>? OnVerifyLoginSuccess;

    public async Task RetryConnection()
    {
        if (_isConnected) return; // Don't retry if already connected

        _logger.LogInformation("Attempting manual connection retry.");
        _connectionError = null;

        // Use internal disconnect first to ensure clean state, but without logging "external request"
        DisconnectInternal("Retry attempt", null);
        await Task.Delay(200); // Brief pause

        try
        {
            // Re-initialize the connection attempt (StartAsync handles builder etc.)
            // Use a new CancellationTokenSource for this specific attempt if needed, or CancellationToken.None
            await StartAsync(CancellationToken.None);
            // Success is handled by OnConnect callback setting _isConnected
        }
        catch (Exception ex)
        {
            _connectionError = $"Connection retry failed: {ex.Message}";
            _logger.LogError(ex, "Error during manual connection retry.");
            // HandleConnectError should be invoked by the failed connection attempt, triggering OnErrorReceived
            // OnErrorReceived?.Invoke("Connection", _connectionError); // Avoid double reporting
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SpacetimeDB Service starting.");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _conn = DbConnection.Builder()
                .WithUri(Host)
                .WithModuleName(Dbname)
                .WithToken(AuthToken.Token)
                .OnConnect(HandleConnect)
                .OnConnectError(HandleConnectError)
                .OnDisconnect(HandleDisconnect)
                .Build();

            RegisterCallbacks(_conn);
            _processTask = Task.Run(() => ProcessLoop(_conn, _cts.Token), _cts.Token);
            _logger.LogInformation("SpacetimeDB connection initiated and processing loop starting.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SpacetimeDB connection.");
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Generic method to handle SpacetimeDB reducer calls with consistent error handling and connection retry
    /// </summary>
    /// <typeparam name="T">Type of parameters to pass to the action (can be a tuple for multiple parameters)</typeparam>
    /// <param name="reducerAction">The action to execute against the SpacetimeDB connection</param>
    /// <param name="parameters">Parameters to pass to the reducer action</param>
    /// <param name="actionName">Name of the action for logging and error reporting</param>
    /// <param name="retryConnection">Whether to retry connection if disconnected</param>
    /// <returns>Task that completes when the operation is done</returns>
    public async Task ExecuteReducerAsync<T>(Action<DbConnection, T> reducerAction, T parameters, string actionName, bool retryConnection = true)
    {
        // If not connected, attempt to establish connection first
        if (!_isConnected || _conn == null)
        {
            _logger.LogInformation($"Not connected to SpacetimeDB. Attempting to connect before {actionName}.");

            if (retryConnection)
            {
                try
                {
                    // Try to reconnect
                    await RetryConnection();

                    // Wait a moment for connection to establish
                    await Task.Delay(1000);

                    // If still not connected after retry, report error
                    if (!_isConnected || _conn == null)
                    {
                        _logger.LogWarning($"Cannot {actionName}: Connection retry failed.");
                        OnErrorReceived?.Invoke("Connection", "Failed to connect to SpacetimeDB. Please try again.");
                        return;
                    }
                }
                catch (Exception connEx)
                {
                    _logger.LogError(connEx, $"Error reconnecting to SpacetimeDB for {actionName}.");
                    OnErrorReceived?.Invoke("Connection", $"Connection error: {connEx.Message}");
                    return;
                }
            }
            else
            {
                _logger.LogWarning($"Cannot {actionName}: Not connected and retry is disabled.");
                OnErrorReceived?.Invoke("Connection", "Not connected to SpacetimeDB.");
                return;
            }
        }

        try
        {
            _logger.LogInformation($"Calling {actionName} reducer with parameters {parameters}");
            reducerAction(_conn, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calling {actionName} reducer.");
            OnErrorReceived?.Invoke("ReducerCall", $"Failed to {actionName}: {ex.Message}");
        }
    }

// Overload for parameterless reducers
    public async Task ExecuteReducerAsync(Action<DbConnection> reducerAction, string actionName, bool retryConnection = true)
    {
        // If not connected, attempt to establish connection first
        if (!_isConnected || _conn == null)
        {
            _logger.LogInformation($"Not connected to SpacetimeDB. Attempting to connect before {actionName}.");

            if (retryConnection)
            {
                try
                {
                    // Try to reconnect
                    await RetryConnection();

                    // Wait a moment for connection to establish
                    await Task.Delay(1000);

                    // If still not connected after retry, report error
                    if (!_isConnected || _conn == null)
                    {
                        _logger.LogWarning($"Cannot {actionName}: Connection retry failed.");
                        OnErrorReceived?.Invoke("Connection", "Failed to connect to SpacetimeDB. Please try again.");
                        return;
                    }
                }
                catch (Exception connEx)
                {
                    _logger.LogError(connEx, $"Error reconnecting to SpacetimeDB for {actionName}.");
                    OnErrorReceived?.Invoke("Connection", $"Connection error: {connEx.Message}");
                    return;
                }
            }
            else
            {
                _logger.LogWarning($"Cannot {actionName}: Not connected and retry is disabled.");
                OnErrorReceived?.Invoke("Connection", "Not connected to SpacetimeDB.");
                return;
            }
        }

        try
        {
            _logger.LogInformation($"Calling {actionName} reducer");
            reducerAction(_conn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calling {actionName} reducer.");
            OnErrorReceived?.Invoke("ReducerCall", $"Failed to {actionName}: {ex.Message}");
        }
    }
}
}
