using SpacetimeDB;
using System.Text.RegularExpressions;

namespace StdbModule.Modules {
using Enums;
// ====================== Auth Module Implementation ======================

public static partial class AuthModule {
    // ====================== Helper Constants & Methods ======================

    private const string EmailPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
    private const string UsernamePattern = @"^[a-zA-Z0-9_-]{3,20}$";
    private static readonly Random Random = new Random();
    // private static readonly EmailService EmailService = new EmailService();

    /// <summary>
    /// Validates an email format
    /// </summary>
    private static bool IsValidEmail(string email)
    {
        return !string.IsNullOrWhiteSpace(email) && Regex.IsMatch(email, EmailPattern);
    }

    /// <summary>
    /// Validates a username format
    /// </summary>
    private static bool IsValidUsername(string username)
    {
        return !string.IsNullOrWhiteSpace(username) && Regex.IsMatch(username, UsernamePattern);
    }

    /// <summary>
    /// Generates a random verification code
    /// </summary>
    private static string GenerateVerificationCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Repeat(chars, 6)
            .Select(s => s[Random.Next(s.Length)]).ToArray());
    }

    // ====================== Lifecycle Reducers ======================

    /// <summary>
    /// Initialize the database with proper hex validation
    /// </summary>
    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info($"Initializing...");
        LogInfo(ctx, ctx.Identity, "Module initialized", "Auth module initialized successfully");
    }

    /// <summary>
    /// Handle client connections
    /// </summary>
    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        Identity identity = ctx.Sender;

        // Assuming the client now provides the auth token as part of the connection metadata.
        // In a real app, you'd have a more robust way to pass this, possibly through a handshake.
        // string? authToken = ctx.Args?.Get("authToken")?.ToString();

        var existingUser = ctx.Db.User.Identity.Find(identity);

        if (existingUser == null)
        {
            Log.Error($"Unauthorized connection attempt.");
            // In a real app, you might want to disconnect the client here.
        }
        else
        {
            LogInfo(ctx, identity, "UserConnected", $"User {existingUser.Username} connected");
        }
    }

    /// <summary>
    /// Handle client disconnections
    /// </summary>
    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        Identity identity = ctx.Sender;

        var existingUser = ctx.Db.User.Identity.Find(identity);

        if (existingUser != null)
        {
            LogInfo(ctx, identity, "UserDisconnected", $"User {existingUser.Username} disconnected");
        }
    }

    // ====================== Registration Reducers ======================

    /// <summary>
    /// Register a new user
    /// </summary>
    [Reducer]
    public static void RegisterUser(ReducerContext ctx, string username, string email, int roleInt)
    {
        Identity identity = ctx.Sender;

        // Validate input
        if (!IsValidUsername(username))
            Log.Error("Invalid username. Username must be 3-20 characters and contain only letters, numbers, underscore or hyphen.");

        if (!IsValidEmail(email))
            Log.Error("Invalid email format.");

        if (!Enum.IsDefined(typeof(UserRole), roleInt))
            Log.Error("Invalid role specified.");

        var role = (UserRole)roleInt;

        // Create a verification code
        string verificationCode = GenerateVerificationCode();

        // Create a TimeDuration object for one day
        var oneDay = new TimeDuration { Microseconds = 86400000000 };

        // Store pending registration
        var pendingReg = new PendingVerification
        {
            Identity = identity,
            Username = username,
            Email = email,
            Role = role,
            VerificationCode = verificationCode,
            ExpiresAt = ctx.Timestamp + oneDay // Expires in 24 hours
        };

        ctx.Db.PendingVerification.Insert(pendingReg);

        LogInfo(ctx, identity, "UserRegistrationRequested",
        $"User registration requested with username {username}, email {email}, role {role}");

        // In a real app, you would send an email with the verification code
        // For now, we just return it to the user
        Log.Info($"Registration successful! Verification code: {verificationCode}");
        // EmailService.SendVerificationEmailAsync(email, verificationCode);
    }

    /// <summary>
    /// Verify email with verification code to complete registration
    /// </summary>
    [Reducer]
    public static void VerifyAccount(ReducerContext ctx, string verificationCode)
    {
        Identity identity = ctx.Sender;

        // Get pending registration
        var pendingVerification = ctx.Db.PendingVerification.Identity.Find(identity) ?? throw new Exception("No pending Verification found.");

        if (pendingVerification?.ExpiresAt < ctx.Timestamp)
        {
            // Check if code has expired
            ctx.Db.PendingVerification.Delete(pendingVerification);
            throw new Exception("Verification code has expired. Please register again."); //TODO Notify User
        }

        if (pendingVerification?.VerificationCode != verificationCode)
        { // Verify the code
            throw new Exception("Invalid verification code."); //TODO Notify User
        }

        // Create the user
        var user = new User
        {
            Identity = identity,
            Username = pendingVerification!.Username,
            Email = pendingVerification.Email,
            Role = pendingVerification.Role,
            IsEmailVerified = true,
            RegisteredAt = ctx.Timestamp
        };

        ctx.Db.User.Insert(user);

        // Create auth session
        var authSession = new AuthSession
        {
            Identity = identity,
            LastActiveTime = ctx.Timestamp,
            ActiveDeviceId = "web" // In a real app, you'd use a device ID
        };

        ctx.Db.AuthSession.Insert(authSession);

        // Remove pending registration
        ctx.Db.PendingVerification.Delete(pendingVerification);

        LogInfo(ctx, identity, "UserRegistrationCompleted",
        $"User registration completed for {user.Username} with role {user.Role}");

        Log.Info($"Email verification successful! You are now registered as a {user.Role}."); //TODO Notify User
    }

    /// <summary>
    /// Request a login verification code for an existing user
    /// </summary>
    [Reducer]
    public static void RequestLoginCode(ReducerContext ctx, string email)
    {
        Identity identity = ctx.Sender;

        // Validate email format
        if (!IsValidEmail(email))
        {
            Log.Error("Invalid email format.");
            throw new Exception("Invalid email format.");
        }

        // Check if user exists with this email
        var user = ctx.Db.User.Identity.Find(identity) ?? throw new Exception("No account found with this email.");

        Log.Info($"Found User! {user}");

        // Generate a verification code
        string verificationCode = GenerateVerificationCode();

        // Create a TimeDuration object for one day
        var oneDay = new TimeDuration { Microseconds = 86400000000 };

        // Store pending registration
        var pendingVerification = new PendingVerification
        {
            Identity = identity,
            Email = email,
            VerificationCode = verificationCode,
            ExpiresAt = ctx.Timestamp + oneDay // Expires in 24 hours (microseconds)
        };

        // Remove any existing pending registration for this identity
        var existingPending = ctx.Db.PendingVerification.Identity.Find(identity);

        if (existingPending != null)
        {
            ctx.Db.PendingVerification.Delete(existingPending);
        }

        ctx.Db.PendingVerification.Insert(pendingVerification);

        LogInfo(ctx, identity, "LoginVerificationRequested",
        $"Login verification requested for {email}");

        // TODO: email with the verification code
        Log.Info($"Verification code sent! {verificationCode}");
    }

    /// <summary>
    /// Verify email with verification code to login
    /// </summary>
    [Reducer]
    public static void VerifyLogin(ReducerContext ctx, string verificationCode, string deviceId = "web")
    {
        Identity identity = ctx.Sender;

        // Get pending registration
        var pendingReg = ctx.Db.PendingVerification.Identity.Find(identity);

        if (pendingReg == null)
        {
            LogInfo(ctx, identity, "LoginFailed", "No pending verification found");
            Log.Info("No pending login verification found. Please request a new code.");
        }


        if (pendingReg.ExpiresAt < ctx.Timestamp)
        {
            ctx.Db.PendingVerification.Delete(pendingReg);
            LogInfo(ctx, identity, "LoginFailed", "Verification code expired");
            Log.Info("Verification code has expired. Please request a new code.");
        }

        // Verify the code
        if (pendingReg.VerificationCode != verificationCode)
        {
            LogInfo(ctx, identity, "LoginAttemptFailed", "Invalid verification code");
            Log.Info("Invalid verification code. Please try again.");
        }

        // Find the user associated with this email
        var user = ctx.Db.User.Identity.Find(identity);
        if (user == null)
        {
            // This should not happen since we checked in RequestLoginCode, but just in case
            ctx.Db.PendingVerification.Delete(pendingReg);
            LogInfo(ctx, identity, "LoginFailed", "User no longer exists");
            Log.Info("Account not found. Please contact support.");
        }

        // If the user's identity doesn't match the current sender,
        // update the user record with the new identity

        if (user.Identity != identity)
        {
            user.Identity = identity;
            ctx.Db.User.Identity.Update(user);
            LogInfo(ctx, identity, "UserIdentityUpdated",
            $"User identity updated for {user.Email}");
        }

        // Check for existing session and update it, or create a new one
        var existingSession = ctx.Db.AuthSession.Identity.Find(identity);
        if (existingSession != null)
        {
            existingSession.LastActiveTime = ctx.Timestamp;
            existingSession.ActiveDeviceId = deviceId;
            ctx.Db.AuthSession.Identity.Update(existingSession);
        }
        else
        {
            // Create new auth session
            var authSession = new AuthSession
            {
                Identity = identity,
                LastActiveTime = ctx.Timestamp,
                ActiveDeviceId = deviceId
            };
            ctx.Db.AuthSession.Insert(authSession);
        }

        // Clean up the pending registration
        ctx.Db.PendingVerification.Delete(pendingReg);

        LogInfo(ctx, identity, "UserLogin", $"User {user.Email} logged in successfully");
        Log.Info("Login successful!");
    }


    // ====================== User Management Reducers ======================

    /// <summary>
    /// Get the current user's profile
    /// </summary>
    [Reducer]
    public static void GetMyProfile(ReducerContext ctx)
    {
        Identity identity = ctx.Sender;

        var user = ctx.Db.User.Identity.Find(identity);
        if (user == null)
            Log.Warn("Not authenticated. Please register first."); // TODO: Notify User

        //TODO: Return user profile without sensitive data
    }

    /// <summary>
    /// Update user profile
    /// </summary>
    [Reducer]
    public static void UpdateProfile(ReducerContext ctx, string email)
    {
        Identity identity = ctx.Sender;

        // Validate user is authenticated

        var user = ctx.Db.User.Identity.Find(identity);

        {
            if (user == null)
                Log.Warn("Not authenticated. Please register first."); //TODO Notify User
        }


        {
            // Validate email
            if (!IsValidEmail(email))
                Log.Warn("Invalid email format.");
        }

        // Check if email is already in use by another user
        var emailExists = ctx.Db.User.Identity.Find(identity);

        if (emailExists != null)
            Log.Warn("Email already in use by another account.");

        // Update user
        user!.Email = email;
        user.IsEmailVerified = false; // Require verification for new email

        ctx.Db.User.Identity.Update(user);

        LogInfo(ctx, identity, "UserProfileUpdated", $"User {user.Username} updated their profile");

        // In a real app, you would send a verification email
        Log.Info("Profile updated successfully. Please verify your new email.");
    }

    // ====================== Role-Specific Functionality ======================

    /// <summary>
    /// List all users with a specific role (only available to auditors)
    /// </summary>
    [Reducer]
    public static void ListUsersByRole(ReducerContext ctx, int roleInt)
    {
        Identity identity = ctx.Sender;

        // Check if user is authenticated and is an auditor
        var user = ctx.Db.User.Identity.Find(identity);

        if (user == null)
            Log.Warn("Not authenticated. Please register first.");

        if (user!.Role != UserRole.Auditor)
            Log.Error("Access denied. Only auditors can list users.");


        // Validate role
        if (!Enum.IsDefined(typeof(UserRole), roleInt))
            Log.Error("Invalid role specified.");

        var role = (UserRole)roleInt;

        LogInfo(ctx, identity, "UsersListed", $"Auditor {user.Username} listed users with role {role}");
    }

    /// <summary>
    /// Get audit logs (only available to auditors)
    /// </summary>
    [Reducer]
    public static void GetAuditLogs(ReducerContext ctx, long startTime, long endTime, int limit)
    {
        Identity identity = ctx.Sender;

        // Check if user is authenticated and is an auditor
        var user = ctx.Db.User.Identity.Find(identity);
        if (user == null)
            Log.Warn("Not authenticated. Please register first.");

        if (user!.Role != UserRole.Auditor)
            Log.Error("Access denied. Only auditors can access audit logs.");

        // Validate input
        if (limit <= 0 || limit > 1000)
            //TODO Return Error  if Greater Than
            LogInfo(ctx, identity, "AuditLogsAccessed",
            $"Auditor {user.Role} accessed audit logs from {startTime} to {endTime}");
    }

    // ====================== Utility Functions ======================

    /// <summary>
    /// Log an info message to the audit log
    /// </summary>
    private static void LogInfo(ReducerContext ctx, Identity identity, string action, string details)
    {
        var log = new AuditLog
        {
            Identity = identity,
            Action = action,
            Details = details,
            Timestamp = ctx.Timestamp
        };

        ctx.Db.AuditLog.Insert(log);
    }

    // ====================== Gamification API (Public Interface) ======================

    // ... (other reducers) ...

    // ... (other reducers) ...

    /// <summary>
    /// Get benefits within a certain radius of a user's location.
    /// </summary>
    [Reducer]
    public static void GetBenefitsByLocation(ReducerContext ctx, double latitude, double longitude, double radiusKm)
    {
        // TODO: Call the BenefitsModule to get benefits within the radius.
    }

    // ... (rest of the code) ...

}

}
