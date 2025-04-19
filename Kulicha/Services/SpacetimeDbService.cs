using SpacetimeDB;
using SpacetimeDB.Types;

namespace Kulicha.Services {
public class SpacetimeDbService : IHostedService, IDisposable {
    private readonly ILogger<SpacetimeDbService> _logger;
    private DbConnection? _conn;
    private CancellationTokenSource? _cts;
    private Task? _processTask;
    private Identity? _localIdentity;
    private volatile bool _isConnected;
    private string? _connectionError;

    // --- Events for Blazor components ---
    public event Action? OnConnect;
    public event Action? OnDisconnect;
    public event Action<Identity>? OnIdentityReceived;
    public event Action<User>? OnProfileReceived;
    public event Action<string, string>? OnErrorReceived; // (type, message)
    public event Action<string>? OnRegisterSuccess;
    public event Action<string>? OnVerifySuccess;
    public event Action<string>? OnVerifyLoginSuccess;
    // ------------------------------------

    public SpacetimeDbService(ILogger<SpacetimeDbService> logger)
    {
        _logger = logger;
        try
        {
            AuthToken.Init(".kulicha");
            _logger.LogInformation("AuthToken initialized.");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to initialize AuthToken."); }
    }

    // --- Public accessors ---
    public bool IsConnected => _isConnected;
    public Identity? LocalIdentity => _localIdentity;
    public string? ConnectionError => _connectionError;
    public bool IsAuthenticated => _localIdentity != null;

    // --- Configuration ---
    private const string Host = "http://127.0.0.1:3000";
    private const string Dbname = "kulicha";
    // -------------------

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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SpacetimeDB Service stopping.");
        if (_cts == null) return;
        try
        {
            if (!_cts.IsCancellationRequested) await _cts.CancelAsync();
        }
        catch (ObjectDisposedException) {}

        if (_processTask is { IsCompleted: false })
        {
            try { await Task.WhenAny(_processTask, Task.Delay(Timeout.Infinite, cancellationToken)); }
            catch (OperationCanceledException) { _logger.LogWarning("Processing loop termination timed out during shutdown."); }
            catch (Exception ex) { _logger.LogError(ex, "Error during processing loop shutdown wait."); }
        }

        DisconnectInternal("Service stopping"); // Ensure disconnect on stop
        _logger.LogInformation("SpacetimeDB Service stopped.");
    }

    private void ProcessLoop(DbConnection conn, CancellationToken ct)
    {
        _logger.LogInformation("SpacetimeDB processing loop started.");
        DateTime lastConnectionAttempt = DateTime.MinValue;
        TimeSpan reconnectInterval = TimeSpan.FromSeconds(10);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                bool currentlyConnected = _conn?.IsActive ?? false; // Check connection state reliably

                if (currentlyConnected)
                {
                    try
                    {
                        conn.FrameTick(); // Process incoming updates
                        if (!_isConnected) // Update state if we just reconnected
                        {
                            HandleConnectInternal(conn); // Use internal handler to set state and invoke event
                        }
                    }
                    catch (InvalidOperationException ioex)
                    {
                        _logger.LogWarning(ioex, "Connection error during FrameTick (likely disconnected).");
                        DisconnectInternal("FrameTick error", ioex);
                    }
                    catch (Exception ftEx) // Catch other FrameTick errors
                    {
                        _logger.LogError(ftEx, "Unexpected error during FrameTick.");
                        DisconnectInternal("FrameTick unexpected error", ftEx);
                    }
                }
                else if (_isConnected || DateTime.UtcNow - lastConnectionAttempt > reconnectInterval) // Only retry if we *were* connected or interval passed
                {
                    if (_isConnected) // If we were connected but conn.IsActive is now false
                    {
                        _logger.LogWarning("Detected potential disconnect in processing loop (IsActive is false).");
                        DisconnectInternal("Detected disconnect"); // Update state and invoke event
                    }

                    _logger.LogInformation("Attempting to reconnect in processing loop...");
                    lastConnectionAttempt = DateTime.UtcNow;
                    // Use Task.Run for non-blocking reconnect attempt
                    _ = Task.Run(RetryConnection, ct); // Fire and forget retry task
                }

                Thread.Sleep(50); // Avoid busy-waiting
            }
        }
        catch (OperationCanceledException) { _logger.LogInformation("SpacetimeDB processing loop cancelled."); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in SpacetimeDB processing loop.");
            DisconnectInternal("Unhandled loop exception", ex);
        }
        finally { _logger.LogInformation("SpacetimeDB processing loop ended."); }
    }

    // --- Generic Reducer Call Method ---
    private async Task CallReducerAsync(string actionName, Action reducerCallAction)
    {
        if (!_isConnected || _conn == null)
        {
            _logger.LogWarning("Not connected to SpacetimeDB. Attempting to connect before calling {ActionName}.", actionName);
            try
            {
                // Attempt connection once, don't loop here, ProcessLoop handles retries
                if (_conn == null) // If connection object itself is null, need to rebuild
                {
                    _logger.LogInformation("Connection object is null, rebuilding connection.");
                    await StartAsync(CancellationToken.None); // Re-initialize connection attempt
                    await Task.Delay(1000); // Give it a moment
                }
                else if (!_conn.IsActive)
                {
                    _logger.LogInformation("Connection object exists but is not active, attempting connect.");

                    await Task.Delay(1000); // Give it a moment
                }


                if (!_isConnected || _conn == null) // Check again after attempt
                {
                    _logger.LogWarning("Cannot call {ActionName}: Connection attempt failed.", actionName);
                    OnErrorReceived?.Invoke("Connection", $"Failed to connect to SpacetimeDB to perform '{actionName}'. Please try again.");
                    return; // Stop execution if connection failed
                }
                _logger.LogInformation("Connection successful before calling {ActionName}.", actionName);
            }
            catch (Exception connEx)
            {
                _logger.LogError(connEx, "Error connecting to SpacetimeDB before calling {ActionName}.", actionName);
                OnErrorReceived?.Invoke("Connection", $"Connection error before '{actionName}': {connEx.Message}");
                return; // Stop execution on connection error
            }
        }

        // If we reach here, _conn should be non-null and _isConnected should be true
        try
        {
            _logger.LogInformation("Calling {ActionName} reducer.", actionName);
            reducerCallAction(); // Execute the provided action
            // Success logging happens in the reducer callback if needed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling {ActionName} reducer.", actionName);
            OnErrorReceived?.Invoke("ReducerCall", $"Failed to call {actionName}: {ex.Message}");
        }
    }

    // --- Public Methods using the Generic Caller ---
    public async Task RequestLoginCode(string email)
    {
        await CallReducerAsync(
        actionName: nameof(RequestLoginCode),
        reducerCallAction: () => _conn!.Reducers.RequestLoginCode(email)
        );
    }

    public async Task VerifyAccount(string verificationCode)
    {
        await CallReducerAsync(
        actionName: nameof(VerifyAccount),
        reducerCallAction: () => _conn!.Reducers.VerifyAccount(verificationCode)
        );
    }

    public async Task Verify(string verificationCode, string? deviceId = null)
    {
        await CallReducerAsync(
        actionName: nameof(Verify),
        reducerCallAction: () => _conn!.Reducers.VerifyLogin(verificationCode, deviceId ?? "web") // Provide default if null
        );
    }

    public async Task UpdateProfile(string newEmail) // Allow updating either or both
    {
        await CallReducerAsync(
        actionName: nameof(UpdateProfile),
        reducerCallAction: () => _conn!.Reducers.UpdateProfile(newEmail)
        );
    }

    public async Task GetMyProfile() // Renamed from GetMyProfile for consistency
    {
        await CallReducerAsync(
        actionName: nameof(GetMyProfile), // Keep reducer name for logging consistency
        reducerCallAction: () => _conn!.Reducers.GetMyProfile()
        );
    }

    public async Task ListUsersByRole(int roleInt)
    {
        await CallReducerAsync(
        actionName: nameof(ListUsersByRole),
        reducerCallAction: () => _conn!.Reducers.ListUsersByRole(roleInt)
        );
    }

    public async Task GetAuditLogs(long startTime, long endTime, int limit)
    {
        await CallReducerAsync(
        actionName: nameof(GetAuditLogs),
        reducerCallAction: () => _conn!.Reducers.GetAuditLogs(startTime, endTime, limit)
        );
    }

    public async Task GetBenefitsByLocation(double latitude, double longitude, double radiusKm)
    {
        await CallReducerAsync(
        actionName: nameof(GetBenefitsByLocation),
        reducerCallAction: () => _conn!.Reducers.GetBenefitsByLocation(latitude, longitude, radiusKm)
        );
    }

    // --- Connection Management ---

    public async Task Logout()
    {
        _logger.LogInformation("User logout requested.");
        DisconnectInternal("User logout");
        // AuthToken.DeleteToken(); // Clear the token on logout
        _localIdentity = null;
        await Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        _logger.LogInformation("External DisconnectAsync called.");
        DisconnectInternal("External request");
        await Task.CompletedTask;
    }

    private void DisconnectInternal(string reason, Exception? ex = null)
    {
        if (!_isConnected && _conn == null) return; // Already disconnected or never connected

        _logger.LogInformation("Internal disconnect initiated. Reason: {Reason}", reason);
        bool wasConnected = _isConnected;
        _isConnected = false; // Set state first
        _localIdentity = null;

        if (_conn != null)
        {
            try
            {
                if (_conn.IsActive) _conn.Disconnect();
            }
            catch (Exception disconnectEx)
            {
                _logger.LogWarning(disconnectEx, "Exception during DisconnectInternal call to _conn.Disconnect().");
            }
            // Consider nulling _conn here or let RetryConnection handle rebuild if needed
            // _conn = null; // If you want to force rebuild on next connect attempt
        }


        if (ex != null)
        {
            _connectionError = $"Disconnected ({reason}): {ex.Message}";
            _logger.LogWarning(ex, "Disconnected abnormally from SpacetimeDB. Reason: {Reason}", reason);
            OnErrorReceived?.Invoke("Connection", _connectionError);
        }
        else
        {
            _connectionError = null; // Clear error on clean disconnect
            _logger.LogInformation("Disconnected cleanly from SpacetimeDB. Reason: {Reason}", reason);
        }

        if (wasConnected)
        { // Only invoke if state changed from connected to disconnected
            OnDisconnect?.Invoke();
        }
    }

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


    // --- Event Handlers ---

    // Internal handler called by ProcessLoop or HandleConnect
    private void HandleConnectInternal(DbConnection conn)
    {
        if (_isConnected) return; // Already handled

        _logger.LogInformation("HandleConnectInternal: Connection established.");
        _isConnected = true;
        _connectionError = null; // Clear any previous error
        // Identity and AuthToken are handled by the external HandleConnect

        OnConnect?.Invoke();

        // Subscribe after successful connection
        try
        {
            conn.SubscriptionBuilder()
                .SubscribeToAllTables();
            _logger.LogInformation("Requested subscription to all tables.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting subscription.");
            OnErrorReceived?.Invoke("Subscription", $"Failed to subscribe: {ex.Message}");
        }
    }

    // External handler called by SpacetimeDB library
    private void HandleConnect(DbConnection conn, Identity identity, string authToken)
    {
        _localIdentity = identity;
        AuthToken.SaveToken(authToken);
        _logger.LogInformation("SpacetimeDB library reported CONNECTED with Identity: {Identity}", identity);
        HandleConnectInternal(conn); // Use internal handler to set state, invoke event, subscribe
        OnIdentityReceived?.Invoke(identity);
    }

    private void HandleConnectError(Exception e)
    {
        _logger.LogError(e, "SpacetimeDB library reported CONNECT ERROR");
        DisconnectInternal("Connect error", e); // Use internal handler
    }

    private void HandleDisconnect(DbConnection conn, Exception? e)
    {
        _logger.LogInformation("SpacetimeDB library reported DISCONNECTED.");
        DisconnectInternal("Library disconnect", e); // Use internal handler
    }

    private void RegisterCallbacks(DbConnection conn)
    {
        conn.Db.User.OnInsert += User_OnInsert;
        conn.Db.User.OnUpdate += User_OnUpdate;
        conn.Db.User.OnDelete += User_OnDelete;

        // Use the simplified reducer names
        conn.Reducers.OnVerifyAccount += OnVerificationCallback;
        conn.Reducers.OnVerifyLogin += OnVerifyCallback;
        conn.Reducers.OnUpdateProfile += OnUpdateProfileCallback;
        conn.Reducers.OnGetMyProfile += OnGetMyProfileCallback;
        conn.Reducers.OnListUsersByRole += OnListUsersByRoleCallback;
        conn.Reducers.OnGetAuditLogs += OnGetAuditLogsCallback;
        conn.Reducers.OnGetBenefitsByLocation += OnGetBenefitsByLocationCallback;


        _logger.LogInformation("Registered SpacetimeDB table and reducer callbacks.");
    }

    // --- Callback Implementations ---

    private string UserNameOrIdentity(User user) => user.Username ?? user.Identity.ToString()[..8];

    private void User_OnInsert(EventContext ctx, User insertedValue)
    {
        _logger.LogInformation("[Event] User Inserted: {User} (Identity: {Identity})", UserNameOrIdentity(insertedValue), insertedValue.Identity);
        if (insertedValue.Identity == _localIdentity)
        {
            OnProfileReceived?.Invoke(insertedValue); // Update local profile on insert
        }
    }

    private void User_OnUpdate(EventContext ctx, User oldValue, User newValue)
    {
        _logger.LogInformation("[Event] User Updated: {User} (Identity: {Identity})", UserNameOrIdentity(newValue), newValue.Identity);
        if (newValue.Identity == _localIdentity)
        {
            OnProfileReceived?.Invoke(newValue); // Update local profile on update
        }
    }

    private void User_OnDelete(EventContext ctx, User deletedValue)
    {
        _logger.LogInformation("[Event] User Deleted: {User} (Identity: {Identity})", UserNameOrIdentity(deletedValue), deletedValue.Identity);
        if (deletedValue.Identity == _localIdentity)
        {
            _logger.LogWarning("Local user was deleted from the database!");
            DisconnectInternal("Local user deleted"); // Trigger disconnect
        }
    }

    // --- Reducer Callback Handlers (Updated for new reducer names) ---

    private void OnVerificationCallback(ReducerEventContext ctx, string verificationCode)
    {
        if (ctx.Event.CallerIdentity != _localIdentity) return;
        HandleReducerCallback(ctx, "VerifyAccount",
        onSuccess: () => _logger.LogInformation("Verification committed for {verificationCode}.", verificationCode),
        onFail: status => _logger.LogWarning("VerifyAccount failed for {verificationCode}: {status}", verificationCode, status)
        );
    }

    private void OnVerifyCallback(ReducerEventContext ctx, string verificationCode, string? deviceId)
    {
        if (ctx.Event.CallerIdentity != _localIdentity) return;
        HandleReducerCallback(ctx, "Verify",
        onSuccess: () => {
            _logger.LogInformation("Verify committed with code {Code} for device {Device}.", verificationCode, deviceId ?? "web");
            // Determine if it was login or register based on whether user existed *before* this commit
            // This is tricky without more state. Let's just raise a generic success.
            // A better approach might be for the Verify reducer to somehow signal intent,
            // or check if the User table insert happened in the *same* transaction.
            OnVerifySuccess?.Invoke("Verification successful"); // Generic success event
        },
        onFail: status => _logger.LogWarning("Verify failed: {Status}", status)
        );
    }

    private void OnUpdateProfileCallback(ReducerEventContext ctx, string? newEmail)
    {
        if (ctx.Event.CallerIdentity != _localIdentity) return;
        HandleReducerCallback(ctx, "UpdateProfile",
        onSuccess: () => _logger.LogInformation("UpdateProfile committed."),
        onFail: status => _logger.LogWarning("UpdateProfile failed: {Status}", status)
        );
    }

    private void OnGetMyProfileCallback(ReducerEventContext ctx) // Assuming GetMyProfile is still the reducer name
    {
        if (ctx.Event.CallerIdentity != _localIdentity) return;
        HandleReducerCallback(ctx, "GetMyProfile",
        onSuccess: () => _logger.LogInformation("GetMyProfile committed. Profile data should sync via table."),
        onFail: status => _logger.LogWarning("GetMyProfile failed: {Status}", status)
        );
    }

    private void OnListUsersByRoleCallback(ReducerEventContext ctx, int roleInt)
    {
        // This might be called by an admin, not necessarily the local user
        // if (ctx.Event.CallerIdentity != _localIdentity) return; // Remove this check if needed

        HandleReducerCallback(ctx, "ListUsersByRole",
        onSuccess: () => _logger.LogInformation("ListUsersByRole committed for role {RoleInt}.", roleInt),
        onFail: status => _logger.LogWarning("ListUsersByRole failed: {Status}", status),
        logIdentity: ctx.Event.CallerIdentity // Log who called it
        );
    }

    private void OnGetAuditLogsCallback(ReducerEventContext ctx, long startTime, long endTime, int limit)
    {
        // if (ctx.Event.CallerIdentity != _localIdentity) return; // Remove if needed

        HandleReducerCallback(ctx, "GetAuditLogs",
        onSuccess: () => _logger.LogInformation("GetAuditLogs committed."),
        onFail: status => _logger.LogWarning("GetAuditLogs failed: {Status}", status),
        logIdentity: ctx.Event.CallerIdentity
        );
    }

    private void OnGetBenefitsByLocationCallback(ReducerEventContext ctx, double latitude, double longitude, double radiusKm)
    {
        if (ctx.Event.CallerIdentity != _localIdentity) return;

        HandleReducerCallback(ctx, "GetBenefitsByLocation",
        onSuccess: () => _logger.LogInformation("GetBenefitsByLocation committed."),
        onFail: status => _logger.LogWarning("GetBenefitsByLocation failed: {Status}", status)
        );
    }


    // --- Generic Reducer Callback Handler ---
    private void HandleReducerCallback(ReducerEventContext ctx, string actionName, Action? onSuccess = null, Action<Status.Failed>? onFail = null, Identity? logIdentity = null)
    {
        var identity = logIdentity ?? ctx.Event.CallerIdentity; // Log against specified or caller identity

        switch (ctx.Event.Status)
        {
            case Status.Committed:
                _logger.LogInformation("Reducer {ActionName} committed for Identity {Identity}.", actionName, identity);
                onSuccess?.Invoke();
                break;
            case Status.Failed failedStatus:
                _logger.LogWarning("Reducer {ActionName} failed for Identity {Identity}: {Status}", actionName, identity, failedStatus);
                onFail?.Invoke(failedStatus);
                // Raise generic error event only if the caller is the local user OR if it's an admin action we want to surface
                if (identity == _localIdentity || actionName == "ListUsersByRole" || actionName == "GetAuditLogs") // Example condition
                {
                    OnErrorReceived?.Invoke(actionName, $"Action failed: {failedStatus}");
                }
                break;
            default:
                _logger.LogError("Reducer {ActionName} for Identity {Identity} had unexpected status: {Status}", actionName, identity, ctx.Event.Status);
                if (identity == _localIdentity)
                {
                    OnErrorReceived?.Invoke(actionName, $"Action failed with unexpected status: {ctx.Event.Status}");
                }
                break;
        }
    }


    public void Dispose()
    {
        _logger.LogInformation("Disposing SpacetimeDB Service.");
        // Cancel processing loop first
        if (_cts is { IsCancellationRequested: false })
        {
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) {}
        }
        DisconnectInternal("Dispose"); // Ensure disconnect on dispose
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
}
