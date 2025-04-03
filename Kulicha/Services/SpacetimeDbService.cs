using SpacetimeDB;
using SpacetimeDB.Types;

namespace Kulicha.Services {
public class SpacetimeDbService : IHostedService, IDisposable {
    private readonly ILogger<SpacetimeDbService> _logger;
    private DbConnection? _conn;
    private CancellationTokenSource? _cts;
    private Task? _processTask;
    private Identity? _localIdentity;
    private bool _isConnected;

    // --- Events for Blazor components ---
    public event Action? OnConnect;
    public event Action? OnDisconnect;
    public event Action<Identity>? OnIdentityReceived;
    public event Action<User>? OnProfileReceived; // Event specifically for profile data
    public event Action<string, string>? OnErrorReceived; // Event for errors (type, message)
    // ------------------------------------

    public SpacetimeDbService(ILogger<SpacetimeDbService> logger)
    {
        _logger = logger;
        // Initialize AuthToken here or ensure it's done before service starts
        // Make sure the directory path is appropriate for your deployment environment
        try
        {
            AuthToken.Init(".kulicha");
            _logger.LogInformation("AuthToken initialized.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AuthToken. Check directory permissions.");
            // Depending on requirements, you might want to throw or handle this differently
        }
    }

    // --- Public accessors ---
    public bool IsConnected => _isConnected;
    public Identity? LocalIdentity => _localIdentity;
    // Removed InputQueue - use specific methods below
    // ----------------------

    // --- Configuration ---
    /// The URI of the SpacetimeDB instance hosting our module.
    private const string Host = "http://127.0.0.1:3000"; // Or your production URL
    /// The module name we chose when we published our module.
    private const string Dbname = "kulicha"; // Ensure this matches your published module name
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
                .WithToken(AuthToken.Token) // Load token if it exists
                .OnConnect(HandleConnect)
                .OnConnectError(HandleConnectError)
                .OnDisconnect(HandleDisconnect)
                .Build();

            // Register callbacks *after* building the connection object
            RegisterCallbacks(_conn);

            // Start the processing loop
            _processTask = Task.Run(() => ProcessLoop(_conn, _cts.Token), _cts.Token);
            _logger.LogInformation("SpacetimeDB connection initiated and processing loop starting.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SpacetimeDB connection.");
            // Optionally prevent application startup if connection is critical
            // throw;
            return Task.CompletedTask; // Or Task.FromException(ex);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SpacetimeDB Service stopping.");

        if (_cts == null) return;

        // Signal cancellation to the processing loop
        try
        {
            if (!_cts.IsCancellationRequested)
            {
                await _cts.CancelAsync();
            }
        }
        catch (ObjectDisposedException)
        { /* Ignore if already disposed */
        }

        // Wait for the processing loop to finish, respecting the host's shutdown token
        if (_processTask is { IsCompleted: false })
        {
            try
            {
                await Task.WhenAny(_processTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Processing loop termination timed out or was cancelled during shutdown.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during processing loop shutdown wait.");
            }
        }

        // Disconnect explicitly
        if (_conn is { IsActive: true }) // Check if active before disconnecting
        {
            try
            {
                _logger.LogInformation("Attempting explicit disconnect from SpacetimeDB.");
                _conn.Disconnect();
            }
            catch (InvalidOperationException ioex)
            {
                _logger.LogWarning(ioex, "Attempted to disconnect a non-connected SpacetimeDB socket during shutdown (might be benign).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during explicit SpacetimeDB disconnection.");
            }
            finally
            {
                _isConnected = false;
                _localIdentity = null;
                OnDisconnect?.Invoke(); // Ensure disconnect event is raised if explicit disconnect happens
            }
        }


        _logger.LogInformation("SpacetimeDB Service stopped.");
    }

    private void ProcessLoop(DbConnection conn, CancellationToken ct)
    {
        _logger.LogInformation("SpacetimeDB processing loop started.");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (conn.IsActive) // Only call FrameTick if connected
                {
                    conn.FrameTick(); // Process incoming updates
                }
                // Removed ProcessCommands - reducer calls are now direct methods
                Thread.Sleep(50); // Avoid busy-waiting
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SpacetimeDB processing loop cancelled.");
        }
        catch (Exception ex)
        {
            // Catch errors from FrameTick if the connection drops unexpectedly
            if (!_isConnected && ex is InvalidOperationException)
            {
                _logger.LogWarning(ex, "SpacetimeDB connection lost during FrameTick (likely benign).");
            }
            else
            {
                _logger.LogError(ex, "Unhandled exception in SpacetimeDB processing loop.");
            }
        }
        finally
        {
            _logger.LogInformation("SpacetimeDB processing loop ended.");
        }
    }

    // --- Public Methods for Reducer Calls ---

    public void RegisterUser(string username, string email, int role, double lat, double lon, string address, string city, string state, string country, string postalCode)
    {
        if (!_isConnected || _conn == null)
        {
            _logger.LogWarning("Cannot RegisterUser: Not connected.");
            OnErrorReceived?.Invoke("Connection", "Not connected to SpacetimeDB.");
            return;
        }
        try
        {
            _logger.LogInformation("Calling RegisterUser reducer for {Username}", username);
            _conn.Reducers.RegisterUser(email, username, role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling RegisterUser reducer.");
            OnErrorReceived?.Invoke("ReducerCall", $"Failed to call RegisterUser: {ex.Message}");
        }
    }

    public void VerifyEmail(string code, string email)
    {
        if (!_isConnected || _conn == null)
        {
            _logger.LogWarning("Cannot VerifyEmail: Not connected.");
            OnErrorReceived?.Invoke("Connection", "Not connected to SpacetimeDB.");
            return;
        }
        try
        {
            _logger.LogInformation("Calling VerifyEmail reducer with code {Code}", code);
            _conn.Reducers.VerifyEmail(email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling VerifyEmail reducer.");
            OnErrorReceived?.Invoke("ReducerCall", $"Failed to call VerifyEmail: {ex.Message}");
        }
    }

    public void UpdateProfile(string email, double lat, double lon, string address, string city, string state, string country, string postalCode)
    {
        if (!_isConnected || _conn == null)
        {
            _logger.LogWarning("Cannot UpdateProfile: Not connected.");
            OnErrorReceived?.Invoke("Connection", "Not connected to SpacetimeDB.");
            return;
        }
        try
        {
            _logger.LogInformation("Calling UpdateProfile reducer for user {Identity}", _localIdentity);
            _conn.Reducers.UpdateProfile(email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling UpdateProfile reducer.");
            OnErrorReceived?.Invoke("ReducerCall", $"Failed to call UpdateProfile: {ex.Message}");
        }
    }

    public void RequestProfile()
    {
        if (!_isConnected || _conn == null)
        {
            _logger.LogWarning("Cannot RequestProfile: Not connected.");
            OnErrorReceived?.Invoke("Connection", "Not connected to SpacetimeDB.");
            return;
        }
        try
        {
            _logger.LogInformation("Calling GetMyProfile reducer for user {Identity}", _localIdentity);
            // GetMyProfile takes no arguments according to the bindings
            _conn.Reducers.GetMyProfile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GetMyProfile reducer.");
            OnErrorReceived?.Invoke("ReducerCall", $"Failed to call GetMyProfile: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (_conn is { IsActive: true })
        {
            _logger.LogInformation("DisconnectAsync called, stopping service.");
            // Trigger the normal shutdown process
            if (_cts is { IsCancellationRequested: false })
            {
                await _cts.CancelAsync();
            }
            // Wait for the process task to complete (optional, depends on desired behavior)
            if (_processTask != null)
            {
                await _processTask;
            }
        }
        else
        {
            _logger.LogInformation("DisconnectAsync called, but already disconnected or not started.");
        }
    }


    // --- Event Handlers ---

    private void HandleConnect(DbConnection conn, Identity identity, string authToken)
    {
        _localIdentity = identity;
        _isConnected = true;
        AuthToken.SaveToken(authToken); // Save the token (potentially new if first connect)
        _logger.LogInformation("Connected to SpacetimeDB with Identity: {Identity}", identity);

        OnConnect?.Invoke();
        OnIdentityReceived?.Invoke(identity);

        // Subscribe after successful connection
        try
        {
            conn.SubscriptionBuilder()
                // .OnApplied(HandleSubscriptionApplied) // Optional: Add if needed
                // .OnError(HandleSubscriptionError)    // Optional: Add if needed
                .SubscribeToAllTables(); // Or specific queries: .Subscribe(new[] { "SELECT * FROM User" });
            _logger.LogInformation("Requested subscription to all tables.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting subscription.");
            OnErrorReceived?.Invoke("Subscription", $"Failed to subscribe: {ex.Message}");
        }
    }

    private void HandleConnectError(Exception e)
    {
        _isConnected = false;
        _localIdentity = null;
        _logger.LogError(e, "Error while connecting to SpacetimeDB");
        OnErrorReceived?.Invoke("Connection", $"Connection failed: {e.Message}");
        OnDisconnect?.Invoke(); // Raise disconnect event on connection error too
    }

    private void HandleDisconnect(DbConnection conn, Exception? e)
    {
        bool wasConnected = _isConnected;
        _isConnected = false;
        _localIdentity = null;

        if (e != null)
        {
            _logger.LogWarning(e, "Disconnected abnormally from SpacetimeDB");
            OnErrorReceived?.Invoke("Connection", $"Disconnected abnormally: {e.Message}");
        }
        else
        {
            _logger.LogInformation("Disconnected normally from SpacetimeDB.");
        }

        if (wasConnected) // Only raise event if we were previously connected
        {
            OnDisconnect?.Invoke();
        }
        // Potentially implement reconnection logic here if desired
    }

    /// Register all the callbacks our app will use.
    private void RegisterCallbacks(DbConnection conn)
    {
        // --- Table Callbacks ---
        conn.Db.User.OnInsert += User_OnInsert;
        conn.Db.User.OnUpdate += User_OnUpdate;
        conn.Db.User.OnDelete += User_OnDelete;
        // Register other table callbacks if needed (e.g., AuditLog)

        // --- Reducer Callbacks ---
        // These are needed to get results/errors back from specific actions
        conn.Reducers.OnGetMyProfile += OnGetMyProfile;
        conn.Reducers.OnRegisterUser += OnRegisterUser;
        conn.Reducers.OnVerifyEmail += OnVerifyEmail;
        conn.Reducers.OnUpdateProfile += OnUpdateProfile;
        // Add other reducer callbacks if needed (e.g., OnListUsersByRole)

        _logger.LogInformation("Registered SpacetimeDB table and reducer callbacks.");
    }

    // --- Callback Implementations ---

    private string UserNameOrIdentity(User user) => user.Username ?? user.Identity.ToString()[..8];

    private void User_OnInsert(EventContext ctx, User insertedValue)
    {
        _logger.LogInformation("[Event] User Inserted: {User} (Identity: {Identity})", UserNameOrIdentity(insertedValue), insertedValue.Identity);
        // Potentially raise an event if Blazor needs to react to *any* user insertion
    }

    private void User_OnUpdate(EventContext ctx, User oldValue, User newValue)
    {
        // Check if the updated user is the local user
        bool isLocalUserUpdate = newValue.Identity == _localIdentity;

        if (oldValue.Username != newValue.Username)
        {
            _logger.LogInformation("[Event] User Renamed: {OldName} -> {NewName} (Identity: {Identity})", UserNameOrIdentity(oldValue), newValue.Username ?? "null", newValue.Identity);
        }
        if (oldValue.IsEmailVerified != newValue.IsEmailVerified)
        {
            _logger.LogInformation("[Event] User Email Verification Changed: {Identity} -> {IsVerified}", newValue.Identity, newValue.IsEmailVerified);
        }
        // Log other relevant changes (Role, Location, etc.)

        // If the profile being updated is the *local* user's profile,
        // raise the OnProfileReceived event again so the UI updates.
        if (isLocalUserUpdate)
        {
            _logger.LogInformation("Local user profile updated via table sync.");
            OnProfileReceived?.Invoke(newValue);
        }
    }

    private void User_OnDelete(EventContext ctx, User deletedValue)
    {
        _logger.LogInformation("[Event] User Deleted: {User} (Identity: {Identity})", UserNameOrIdentity(deletedValue), deletedValue.Identity);
        // If the deleted user is the local user, trigger disconnect state
        if (deletedValue.Identity == _localIdentity)
        {
            _logger.LogWarning("Local user was deleted from the database!");
            _isConnected = false;
            _localIdentity = null;
            // Force disconnect/logout on the client side
            OnDisconnect?.Invoke();
            // Optionally trigger reconnection or redirect to Auth
        }
    }


    // --- Reducer Callback Handlers ---

    // Callback for GetMyProfile (Response containing User data)
    private void OnGetMyProfile(ReducerEventContext ctx, Reducer.GetMyProfile? args)
    {
        if (ctx.Event.Status is Status.Committed)
        {
            _logger.LogInformation("GetMyProfile reducer committed successfully.");
            // We need to query the User table *after* the commit to get the data,
            // as GetMyProfile itself doesn't return the User object in its args according to bindings.
            // The result should trigger the User_OnUpdate callback if the profile changed,
            // or we can query it directly here. Let's rely on OnUpdate for now.
            // If GetMyProfile *did* return the user, we'd raise OnProfileReceived here.
            // --- Example if it returned data: ---
            // if (args.ReturnedUser != null) { // Fictional returned data
            //      _logger.LogInformation("Profile data received for {Identity}", args.ReturnedUser.Identity);
            //      OnProfileReceived?.Invoke(args.ReturnedUser);
            // } else {
            //      _logger.LogWarning("GetMyProfile committed but returned no user data?");
            // }
            // --- Since it doesn't return data directly based on bindings: ---
            _logger.LogInformation("GetMyProfile committed. Profile data (if changed) will arrive via table sync.");
            // Maybe trigger a UI refresh hint? Or just rely on User_OnUpdate.
        }
        else if (ctx.Event.Status is Status.Failed("error"))
        {
            _logger.LogError("GetMyProfile reducer failed: {Error}", "error");
            OnErrorReceived?.Invoke("GetMyProfile", $"Failed to get profile: {ctx.Event.Status}");
        }
    }

    // Callback for RegisterUser
    private void OnRegisterUser(ReducerEventContext ctx, Reducer.RegisterUser args)
    {
        switch (ctx.Event.Status)
        {
            case Status.Committed:
                _logger.LogInformation("RegisterUser reducer committed successfully for {Username}.", args.Username);
                // Maybe raise a success event if needed by UI
                break;
            case Status.Failed("error"):
                _logger.LogError("RegisterUser reducer failed for {Username}: {Error}", args.Username, ctx.Event.Status);
                OnErrorReceived?.Invoke("RegisterUser", $"Registration failed: {ctx.Event.Status}");
                break;
        }

    }

    // Callback for VerifyEmail
    private void OnVerifyEmail(ReducerEventContext ctx, Reducer.VerifyEmail args)
    {
        switch (ctx.Event.Status)
        {
            case Status.Committed:
                _logger.LogInformation("VerifyEmail reducer committed successfully with code {Code}.", args.VerificationCode);
                // Maybe raise a success event
                break;
            case Status.Failed("error"):
                _logger.LogError("VerifyEmail reducer failed with code {Code}: {Error}", args.VerificationCode, ctx.Event.Status);
                OnErrorReceived?.Invoke("VerifyEmail", $"Email verification failed: {ctx.Event.Status}");
                break;
        }
    }

    // Callback for UpdateProfile
    private void OnUpdateProfile(ReducerEventContext ctx, Reducer.UpdateProfile args)
    {
        switch (ctx.Event.Status)
        {
            case Status.Committed:
                _logger.LogInformation("UpdateProfile reducer committed successfully for email {Email}.", args.Email);
                // Profile data will be updated via the User_OnUpdate callback triggering OnProfileReceived
                break;
            case Status.Failed("error"):
                _logger.LogError("UpdateProfile reducer failed for email {Email}: {Error}", args.Email, ctx.Event.Status);
                OnErrorReceived?.Invoke("UpdateProfile", $"Profile update failed: {ctx.Event.Status}");
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
        // Disconnect (StopAsync should handle waiting, but ensure disconnect is attempted)
        if (_conn is { IsActive: true })
        {
            try { _conn.Disconnect(); }
            catch (InvalidOperationException) {}
            catch (Exception ex) { _logger.LogError(ex, "Error during dispose disconnect."); }
        }
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
}
