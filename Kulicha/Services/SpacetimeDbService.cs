using SpacetimeDB;
using SpacetimeDB.Types;

namespace Kulicha.Services
{
    public class SpacetimeDbService : IHostedService, IDisposable
    {
        private readonly ILogger<SpacetimeDbService> _logger;
        private DbConnection? _conn;
        private CancellationTokenSource? _cts;
        private Task? _processTask;
        private Identity? _localIdentity;
        private bool _isConnected;
        private string? _connectionError;

        // --- Events for Blazor components ---
        public event Action? OnConnect;
        public event Action? OnDisconnect;
        public event Action<Identity>? OnIdentityReceived;
        public event Action<User>? OnProfileReceived; // Event specifically for profile data
        public event Action<string, string>? OnErrorReceived; // Event for errors (type, message)
        public event Action<string>? OnRegisterSuccess; // Event for successful registration

        public event Action<string>? OnVerifySuccess; // Event for successful registration

        public event Action<string>? OnVerifyLoginSuccess; // Event for successful registration


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
                _logger.LogInformation(ex, "Failed to initialize AuthToken. Check directory permissions.");
                // Depending on requirements, you might want to throw or handle this differently
            }
        }

        // --- Public accessors ---
        public bool IsConnected => _isConnected;
        public Identity? LocalIdentity => _localIdentity;
        public string? ConnectionError => _connectionError;
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
                   .WithToken(AuthToken.Token)
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
                _logger.LogInformation(ex, "Failed to initialize SpacetimeDB connection.");
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
                    _logger.LogInformation(ex, "Error during processing loop shutdown wait.");
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
                    _logger.LogInformation(ex, "Error during explicit SpacetimeDB disconnection.");
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
            DateTime lastConnectionAttempt = DateTime.MinValue;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (conn.IsActive) // Only call FrameTick if connected
                    {
                        try
                        {
                            conn.FrameTick(); // Process incoming updates

                            // If we were previously disconnected, update state
                            if (!_isConnected)
                            {
                                _logger.LogInformation("Connection restored in processing loop.");
                                _isConnected = true;
                                OnConnect?.Invoke();
                            }
                        }
                        catch (InvalidOperationException ioex)
                        {
                            // Connection might have been lost during FrameTick
                            _logger.LogWarning(ioex, "Connection error during FrameTick.");
                            _isConnected = false;
                            OnDisconnect?.Invoke();
                        }
                    }
                    else if (!_isConnected && DateTime.Now - lastConnectionAttempt > TimeSpan.FromSeconds(10))
                    {
                        // Try to reconnect every 10 seconds if not connected
                        _logger.LogInformation("Attempting to reconnect in processing loop...");
                        lastConnectionAttempt = DateTime.Now;
                        try
                        {
                            // Attempt to reconnect without blocking the processing loop
                            Task.Run(async () => await RetryConnection()).ConfigureAwait(false);
                        }
                        catch (Exception reconnectEx)
                        {
                            _logger.LogWarning(reconnectEx, "Failed to reconnect in processing loop.");
                        }
                    }

                    // Avoid busy-waiting
                    Thread.Sleep(50);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SpacetimeDB processing loop cancelled.");
            }
            catch (Exception ex)
            {
                // Catch other unexpected errors
                _logger.LogInformation(ex, "Unhandled exception in SpacetimeDB processing loop.");
                _isConnected = false;
                OnDisconnect?.Invoke();
            }
            finally
            {
                _logger.LogInformation("SpacetimeDB processing loop ended.");
            }
        }

        // --- Public Methods for Reducer Calls ---

        /// <summary>
        /// Requests a login verification code for an existing user's email
        /// </summary>
        /// <param name="email">The email address to send the verification code to</param>
        public async void RequestLoginCode(string email)
        {
            // If not connected, attempt to establish connection first
            if (!_isConnected || _conn == null)
            {
                _logger.LogInformation("Not connected to SpacetimeDB. Attempting to connect before registration.");
                try
                {
                    // Try to reconnect
                    await RetryConnection();

                    // Wait a moment for connection to establish
                    await Task.Delay(1000);

                    // If still not connected after retry, report error
                    if (!_isConnected || _conn == null)
                    {
                        _logger.LogWarning("Cannot RequestLoginCoder: Connection retry failed.");
                        OnErrorReceived?.Invoke("Connection", "Failed to connect to SpacetimeDB. Please try again.");
                        return;
                    }
                }
                catch (Exception connEx)
                {
                    _logger.LogInformation(connEx, "Error reconnecting to SpacetimeDB.");
                    OnErrorReceived?.Invoke("Connection", $"Connection error: {connEx.Message}");
                    return;
                }
            }

            try
            {
                _logger.LogInformation("Calling RequestLoginCode reducer for email {Email}", email);
                _conn.Reducers.RequestLoginCode(email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling RequestLoginCode reducer.");
                OnErrorReceived?.Invoke("ReducerCall", $"Failed to request login code: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies a login using the provided verification code
        /// </summary>
        /// <param name="verificationCode">The verification code received via email</param>
        /// <param name="deviceId">Optional device identifier, defaults to "web" if not provided</param>
        public async void VerifyLogin(string verificationCode, string deviceId = "web")
        {
            // If not connected, attempt to establish connection first
            if (!_isConnected || _conn == null)
            {
                _logger.LogInformation("Not connected to SpacetimeDB. Attempting to connect before registration.");
                try
                {
                    // Try to reconnect
                    await RetryConnection();

                    // Wait a moment for connection to establish
                    await Task.Delay(1000);

                    // If still not connected after retry, report error
                    if (!_isConnected || _conn == null)
                    {
                        _logger.LogWarning("Cannot RegisterUser: Connection retry failed.");
                        OnErrorReceived?.Invoke("Connection", "Failed to connect to SpacetimeDB. Please try again.");
                        return;
                    }
                }
                catch (Exception connEx)
                {
                    _logger.LogInformation(connEx, "Error reconnecting to SpacetimeDB.");
                    OnErrorReceived?.Invoke("Connection", $"Connection error: {connEx.Message}");
                    return;
                }
            }

            try
            {
                _logger.LogInformation("Calling VerifyLogin reducer with code for device {DeviceId}", deviceId);
                _conn.Reducers.VerifyLogin(verificationCode, deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling VerifyLogin reducer.");
                OnErrorReceived?.Invoke("ReducerCall", $"Failed to verify login: {ex.Message}");
            }
        }

        /// <summary>
        /// Generate a device identifier based on browser information
        /// </summary>
        private string GenerateDeviceId()
        {
            // In a real implementation, this would use browser fingerprinting
            // or a combination of user agent, screen resolution, etc.
            // For simplicity, we'll just use a basic identifier here
            return $"web-{DateTime.UtcNow.Ticks}";
        }

        public async void RegisterUser(string username, string email, int role)
        {
            // If not connected, attempt to establish connection first
            if (!_isConnected || _conn == null)
            {
                _logger.LogInformation("Not connected to SpacetimeDB. Attempting to connect before registration.");
                try
                {
                    // Try to reconnect
                    await RetryConnection();

                    // Wait a moment for connection to establish
                    await Task.Delay(1000);

                    // If still not connected after retry, report error
                    if (!_isConnected || _conn == null)
                    {
                        _logger.LogWarning("Cannot RegisterUser: Connection retry failed.");
                        OnErrorReceived?.Invoke("Connection", "Failed to connect to SpacetimeDB. Please try again.");
                        return;
                    }
                }
                catch (Exception connEx)
                {
                    _logger.LogInformation(connEx, "Error reconnecting to SpacetimeDB.");
                    OnErrorReceived?.Invoke("Connection", $"Connection error: {connEx.Message}");
                    return;
                }
            }

            try
            {
                _logger.LogInformation("Calling RegisterUser reducer for {Username}", username);
                _conn.Reducers.RegisterUser(username, email, role);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Error calling RegisterUser reducer.");
                OnErrorReceived?.Invoke("ReducerCall", $"Failed to call RegisterUser: {ex.Message}");
            }
        }

        public async void VerifyAccount(string code)
        {
            // If not connected, attempt to establish connection first
            if (!_isConnected || _conn == null)
            {
                _logger.LogWarning("Not connected to SpacetimeDB. Attempting to connect before verification.");
                try
                {
                    // Try to reconnect
                    await RetryConnection();

                    // Wait a moment for connection to establish
                    await Task.Delay(1000);

                    // If still not connected after retry, report error
                    if (!_isConnected || _conn == null)
                    {
                        _logger.LogWarning("Cannot VerifyAccount: Connection retry failed.");
                        OnErrorReceived?.Invoke("Connection", "Failed to connect to SpacetimeDB. Please try again.");
                        return;
                    }
                }
                catch (Exception connEx)
                {
                    _logger.LogInformation(connEx, "Error reconnecting to SpacetimeDB.");
                    OnErrorReceived?.Invoke("Connection", $"Connection error: {connEx.Message}");
                    return;
                }
            }

            try
            {
                _logger.LogInformation("Calling VerifyAccount reducer with code {Code}", code);
                // Pass the verification code
                _conn.Reducers.VerifyAccount(code);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Error calling VerifyAccount reducer.");
                OnErrorReceived?.Invoke("ReducerCall", $"Failed to call VerifyAccountl: {ex.Message}");
            }
        }

        public void UpdateProfile(string email)
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
                // Pass all location parameters to the UpdateProfile reducer
                _conn.Reducers.UpdateProfile(email);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Error calling UpdateProfile reducer.");
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
                _logger.LogInformation(ex, "Error calling GetMyProfile reducer.");
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

        public async Task RetryConnection()
        {
            _logger.LogInformation("Attempting to retry connection to SpacetimeDB.");
            _connectionError = null;

            try
            {
                // First ensure we're disconnected
                await DisconnectAsync();

                // Small delay to ensure disconnect completes
                await Task.Delay(500);

                // Attempt to reconnect
                await StartAsync(CancellationToken.None);

                _logger.LogInformation("Connection retry initiated successfully.");
            }
            catch (Exception ex)
            {
                _connectionError = $"Connection retry failed: {ex.Message}";
                _logger.LogInformation(ex, "Error during connection retry.");
                OnErrorReceived?.Invoke("Connection", _connectionError);
                throw; // Rethrow to allow caller to handle
            }
        }


        // --- Event Handlers ---

        private void HandleConnect(DbConnection conn, Identity identity, string authToken)
        {
            _localIdentity = identity;
            _isConnected = true;;
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
                _logger.LogInformation(ex, "Error requesting subscription.");
                OnErrorReceived?.Invoke("Subscription", $"Failed to subscribe: {ex.Message}");
            }
        }

        private void HandleConnectError(Exception e)
        {
            _isConnected = false;
            _localIdentity = null;
            _connectionError = $"Connection failed: {e.Message}";
            _logger.LogInformation(e, "Error while connecting to SpacetimeDB");
            OnErrorReceived?.Invoke("Connection", _connectionError);
            OnDisconnect?.Invoke(); // Raise disconnect event on connection error too
        }

        private void HandleDisconnect(DbConnection conn, Exception? e)
        {
            bool wasConnected = _isConnected;
            _isConnected = false;
            _localIdentity = null;

            if (e != null)
            {
                _connectionError = $"Disconnected abnormally: {e.Message}";
                _logger.LogWarning(e, "Disconnected abnormally from SpacetimeDB");
                OnErrorReceived?.Invoke("Connection", _connectionError);
            }
            else
            {
                _connectionError = null;
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
            conn.Reducers.OnRegisterUser += OnRegisterUserCallback;
            conn.Reducers.OnVerifyAccount += OnVerifyAccountCallback;
            conn.Reducers.OnUpdateProfile += OnUpdateProfileCallback;
            conn.Reducers.OnRequestLoginCode += OnRequestLoginCodeCallback;
            conn.Reducers.OnVerifyLogin += OnVerifyLoginCallback;
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
        private void OnGetMyProfile(ReducerEventContext ctx)
        {
            if (ctx.Event.CallerIdentity != _localIdentity) return;

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
                _logger.LogInformation("GetMyProfile reducer failed: {Error}", "error");
                OnErrorReceived?.Invoke("GetMyProfile", $"Failed to get profile: {ctx.Event.Status}");
            }
        }

        // Callback for RegisterUser
        private void OnRegisterUserCallback(ReducerEventContext ctx, string username, string email, int roleInt) // Added args parameter
        {
            if (ctx.Event.CallerIdentity != _localIdentity) return;

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    _logger.LogInformation("RegisterUser reducer committed successfully for {Username}.", username);
                    // User data will arrive via User_OnInsert, which calls OnProfileReceived
                    OnRegisterSuccess?.Invoke(username);
                    break;
                case Status.Failed status:
                    var errorMessage = status.ToString();
                    if (errorMessage.Contains("UniqueConstraintViolationException"))
                    {
                        errorMessage = "This email or username is already registered.";
                    }
                    _logger.LogInformation("RegisterUser reducer failed for: {Error}", ctx.Event.Status);
                    OnErrorReceived?.Invoke("RegisterUser", $"Registration failed: {ctx.Event.Status}");
                    break;
            }
        }

        // Callback for VerifyEmail
        private void OnVerifyAccountCallback(ReducerEventContext ctx, string code) // Parameter is the verification code
        {
            if (ctx.Event.CallerIdentity != _localIdentity) return;

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    _logger.LogInformation("VerifyAccount reducer committed successfully with code {Code}.", code);
                    // User data update (IsEmailVerified=true) will arrive via User_OnUpdate
                    // Extract email from the user record or use a stored value
                    var user = _conn?.Db.User.Identity.Find(ctx.Event.CallerIdentity);
                    _logger.LogInformation("Account Belongs to: {Error}", ctx.Event.CallerIdentity);
                    string email = user?.Email ?? "unknown";
                    OnVerifySuccess?.Invoke(email);
                    break;
                case Status.Failed:
                    _logger.LogInformation("VerifyAccount reducer failed: {Error}", ctx.Event.Status);
                    OnErrorReceived?.Invoke("VerifyAccount", $"Account verification failed: {ctx.Event.Status}");
                    break;
            }
        }


        // Callback for RequestLogin
        private void OnRequestLoginCodeCallback(ReducerEventContext ctx, string email) // Parameter is the verification code
        {
            if (ctx.Event.CallerIdentity != _localIdentity) return;

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    _logger.LogInformation("Login Request reducer committed successfully for email {email}.", email);
                    OnVerifyLoginSuccess?.Invoke(email); // Use the correct event for login verification
                    break;
                case Status.Failed:
                    _logger.LogInformation("Login Request reducer failed: {Error}", ctx.Event.Status);
                    OnErrorReceived?.Invoke("Login Request", $"Email verification failed: {ctx.Event.Status}");
                    break;
            }
        }

        // Callback for UpdateProfile
        private void OnUpdateProfileCallback(ReducerEventContext ctx, string email) // Added args parameter
        {
            if (ctx.Event.CallerIdentity != _localIdentity) return;

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    _logger.LogInformation("UpdateProfile reducer committed successfully for email {Email}.", ctx.Event.Status);
                    // Profile data update will arrive via the User_OnUpdate callback triggering OnProfileReceived
                    break;
                case Status.Failed:
                    _logger.LogInformation("UpdateProfile reducer failed for email: {Error}", ctx.Event.Status);
                    OnErrorReceived?.Invoke("UpdateProfile", $"Profile update failed: {ctx.Event.Status}");
                    break;
            }
        }

        // Callback for VerifyLogin
        private void OnVerifyLoginCallback(ReducerEventContext ctx, string verificationCode, string deviceId)
        {
            if (ctx.Event.CallerIdentity != _localIdentity) return;

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    // The result from the reducer should be available in ctx.Event.Result
                    string result = ctx.Event.Status?.ToString() ?? "Login successful!";

                    // Check if the result contains an error message
                    if (result.Contains("failed") || result.Contains("invalid") || result.Contains("expired"))
                    {
                        _logger.LogWarning("VerifyLogin failed with message: {Result}", result);
                        OnErrorReceived?.Invoke("VerifyLogin", result);
                        return;
                    }

                    _logger.LogInformation("VerifyLogin successful for device {DeviceId}", deviceId);

                    // The deviceId is passed through from the verification code submission
                    // It should be stored or used to identify this session
                    OnVerifyLoginSuccess?.Invoke(deviceId);
                    break;

                case Status.Failed:
                    _logger.LogError("VerifyLogin reducer execution failed: {Error}", ctx.Event.Status);
                    OnErrorReceived?.Invoke("VerifyLogin", $"Email verification failed: {ctx.Event.Status}");
                    break;

                default:
                    _logger.LogWarning("VerifyLogin reducer returned unexpected status: {Status}", ctx.Event.Status);
                    OnErrorReceived?.Invoke("VerifyLogin", $"Unexpected verification status: {ctx.Event.Status}");
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
                catch (ObjectDisposedException) { }
            }
            // Disconnect (StopAsync should handle waiting, but ensure disconnect is attempted)
            if (_conn is { IsActive: true })
            {
                try { _conn.Disconnect(); }
                catch (InvalidOperationException) { }
                catch (Exception ex) { _logger.LogInformation(ex, "Error during dispose disconnect."); }
            }
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
