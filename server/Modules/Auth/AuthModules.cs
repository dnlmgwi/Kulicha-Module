using SpacetimeDB;
using System.Text.RegularExpressions;

namespace Server.Modules.Auth {
using Enums;
public static partial class AuthModule {
    // ====================== Helper Constants & Methods ======================

    private const string EmailPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
    private const string UsernamePattern = @"^[a-zA-Z0-9_-]{3,20}$"; // Keep for registration validation
    private static readonly Random Random = new Random();
    private static readonly TimeDuration VerificationExpiry = new TimeDuration { Microseconds = 86400000000 / 4 }; // 6 hours expiry

    private static bool IsValidEmail(string email) => !string.IsNullOrWhiteSpace(email) && Regex.IsMatch(email, EmailPattern);
    private static bool IsValidUsername(string username) => !string.IsNullOrWhiteSpace(username) && Regex.IsMatch(username, UsernamePattern);

    private static string GenerateVerificationCode()
    {
        const string chars = "0123456789"; // Numeric codes are often easier
        return new string(Enumerable.Repeat(chars, 6).Select(s => s[Random.Next(s.Length)]).ToArray());
    }

    // ====================== Utility: Logging ======================

    private static void LogAuthAction(ReducerContext ctx, string action, string details, Identity? targetIdentity = null)
    {
        var identityToLog = targetIdentity ?? ctx.Sender; // Log against target or sender
        Log.Info($"Auth Action by {ctx.Sender} (Target: {identityToLog}): {action} - {details}");
        ctx.Db.AuditLog.Insert(new AuditLog
        {
            Identity = identityToLog,
            Action = action,
            Details = details,
            Timestamp = ctx.Timestamp
        });
    }

    // ====================== Lifecycle Reducers (Unchanged) ======================

    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        var user = ctx.Db.User.Identity.Find(ctx.Sender); // Use Pk for Identity lookup
        if (user != null)
        {
            LogAuthAction(ctx, "UserConnected", $"User {user.Username} connected.");
            // Optionally update LastActiveTime in AuthSession here if desired
        }
        else
        {
            Log.Info($"Auth: New client connected with Identity {ctx.Sender}. No user record found yet.");
        }
    }

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        var user = ctx.Db.User.Identity.Find(ctx.Sender); // Use Pk for Identity lookup
        if (user != null)
        {
            LogAuthAction(ctx, "UserDisconnected", $"User {user.Username} disconnected.");
            // Optionally update LastActiveTime or clear session info here
        }
        else
        {
            Log.Info($"Auth: Client with Identity {ctx.Sender} disconnected. No user record found.");
        }
    }

    // ====================== Simplified Auth Flow Reducers ======================

    /// <summary>
    /// Initiates registration or login by sending a verification code to the user's email.
    /// Determines intent (register vs login) based on email existence.
    /// </summary>
    [Reducer]
    public static void RequestVerification(ReducerContext ctx, string email, string? username = null, int? roleInt = null)
    {
        Identity identity = ctx.Sender;
        email = email.ToLowerInvariant().Trim(); // Normalize email

        if (!IsValidEmail(email))
        {
            throw new Exception("Invalid email format provided.");
        }

        // Determine intent and validate accordingly
        bool isRegistrationAttempt = username != null || roleInt != null;
        User? existingUserByEmail = ctx.Db.User.Iter().FirstOrDefault(u => u.Email == email);
        User? existingUserById = ctx.Db.User.Identity.Find(identity); // Check if current identity already has a user

        // Prevent logged-in users from initiating verification for a *different* email/account
        if (existingUserById != null && existingUserById.Email != email)
        {
            throw new Exception("Cannot request verification for a different email while logged in.");
        }


        string? validatedUsername = null;

        var validatedRole = (UserRole)roleInt!;

        if (isRegistrationAttempt)
        {
            // --- REGISTRATION VALIDATION ---
            if (existingUserByEmail != null)
            {
                throw new Exception($"Email '{email}' is already registered. Please login instead.");
            }
            if (string.IsNullOrWhiteSpace(username) || roleInt == null)
            {
                throw new Exception("Username and Role are required for registration.");
            }
            if (!IsValidUsername(username))
            {
                throw new Exception("Invalid username format (3-20 chars, letters, numbers, _, -).");
            }
            if (ctx.Db.User.Iter().Any(u => u.Username == username))
            {
                throw new Exception($"Username '{username}' is already taken.");
            }
            if (!Enum.IsDefined(typeof(UserRole), roleInt.Value))
            {
                throw new Exception("Invalid user role specified.");
            }
            validatedUsername = username;
            validatedRole = (UserRole)roleInt;
            LogAuthAction(ctx, "RegistrationVerificationRequested", $"Attempting registration for {email} / {username}");
        }
        else
        {
            // --- LOGIN VALIDATION ---
            if (existingUserByEmail == null)
            {
                throw new Exception($"No account found for email '{email}'. Please register first.");
            }
            LogAuthAction(ctx, "LoginVerificationRequested", $"Attempting login for {email}");
        }

        // --- Generate and Store Code ---
        string verificationCode = GenerateVerificationCode();
        Timestamp expiresAt = ctx.Timestamp + VerificationExpiry;

        // Upsert PendingVerification: Delete existing for this identity, then insert new.
        var existingPending = ctx.Db.PendingVerification.Identity.Find(identity);
        if (existingPending != null)
        {
            ctx.Db.PendingVerification.Delete(existingPending);
        }


        ctx.Db.PendingVerification.Insert(new PendingVerification
        {
            Identity = identity,
            Email = email, // Store normalized email
            Username = validatedUsername, // Null for login attempts
            Role = validatedRole, // Null for login attempts
            VerificationCode = verificationCode,
            ExpiresAt = expiresAt
        });

        // --- Send Email (Conceptual) ---
        // EmailService.SendVerificationEmailAsync(email, verificationCode);
        Log.Info($"Auth: Verification code generated for {email}. Code: {verificationCode} (Expires: {expiresAt})"); // Log code for testing
        // Consider returning a success message, but not the code itself.
    }

    /// <summary>
    /// Verifies the code sent to the user's email to complete registration or login.
    /// </summary>
    [Reducer]
    public static void Verify(ReducerContext ctx, string verificationCode, string? deviceId)
    {
        Identity identity = ctx.Sender;

        // 1. Find Pending Verification
        var pending = ctx.Db.PendingVerification.Identity.Find(identity); // Use Pk

        if (pending == null)
        {
            throw new Exception("No pending verification found. Please request a code first.");
        }

        // 2. Check Expiry
        if (pending.ExpiresAt < ctx.Timestamp)
        {
            ctx.Db.PendingVerification.Delete(pending); // Clean up expired entry
            throw new Exception("Verification code has expired. Please request a new one.");
        }

        // 3. Check Code Match
        if (pending.VerificationCode != verificationCode)
        {
            // Consider adding rate limiting or attempts logic here in a real app
            throw new Exception("Invalid verification code.");
        }

        // 4. Verification successful - Determine action: Register or Login
        User? user = ctx.Db.User.Identity.Find(identity); // Use Pk

        if (user == null)
        {
            // --- COMPLETE REGISTRATION ---

            // Explicitly check for required registration fields from PendingVerification
            // These should have been validated and stored by RequestVerification
            if (string.IsNullOrEmpty(pending.Username) || string.IsNullOrEmpty(pending.Email))
            {
                // This indicates an inconsistent state, likely an issue in RequestVerification
                ctx.Db.PendingVerification.Delete(pending); // Clean up inconsistent state
                Log.Error($"Auth Error: Pending verification data incomplete for registration. Identity: {identity}");
                throw new Exception("Internal error during registration. Please try again or contact support.");
            }

            // Double-check if email/username got taken *after* request but *before* verify (race condition)
            // Use the non-null values checked above
            if (ctx.Db.User.Iter().Any(u => u.Email == pending.Email))
            {
                ctx.Db.PendingVerification.Delete(pending);
                throw new Exception($"Email '{pending.Email}' was registered by someone else. Please try registering again.");
            }

            if (ctx.Db.User.Iter().Any(u => u.Username == pending.Username))
            {
                ctx.Db.PendingVerification.Delete(pending);
                throw new Exception($"Username '{pending.Username}' was taken. Please try registering again with a different username.");
            }

            user = new User
            {
                Identity = identity,
                Username = pending.Username,
                Email = pending.Email,
                Role = pending.Role,
                IsEmailVerified = true,
                RegisteredAt = ctx.Timestamp
            };
            ctx.Db.User.Insert(user);
            LogAuthAction(ctx, "UserRegistrationCompleted", $"User {user.Username} ({user.Email}) completed registration.");

            // Create initial AuthSession
            ctx.Db.AuthSession.Insert(new AuthSession
            {
                Identity = identity,
                LastActiveTime = ctx.Timestamp,
                ActiveDeviceId = deviceId ?? "unknown"
            });
            LogAuthAction(ctx, "SessionCreated", $"Initial session created for {user.Username}. Device: {deviceId ?? "unknown"}");
        }
        else
        {
            // --- COMPLETE LOGIN ---

            // Sanity check: Ensure the email in the pending record matches the logged-in user's email
            // Also check if pending.Email is null, although it shouldn't be for a login verification
            if (string.IsNullOrEmpty(pending.Email) || user.Email != pending.Email)
            {
                // This implies the user somehow requested verification for one email (or no email stored?)
                // while their identity is already linked to a different verified email.
                ctx.Db.PendingVerification.Delete(pending); // Clean up
                Log.Warn($"Auth Mismatch: Verify attempted for email '{pending.Email ?? "NULL"}' but user '{user.Username}' has email '{user.Email}'. Identity: {identity}");
                throw new Exception("Verification email does not match the email associated with your account.");
            }

            LogAuthAction(ctx, "UserLogin", $"User {user.Username} ({user.Email}) logged in.");

            // Upsert AuthSession
            var session = ctx.Db.AuthSession.Identity.Find(identity); // Use Pk
            if (session != null)
            {
                session.LastActiveTime = ctx.Timestamp;
                session.ActiveDeviceId = deviceId ?? "unknown";
                ctx.Db.AuthSession.Identity.Update(session); // Use Update with object
                LogAuthAction(ctx, "SessionUpdated", $"Session updated for {user.Username}. Device: {deviceId ?? "unknown"}");
            }
            else
            {
                ctx.Db.AuthSession.Insert(new AuthSession
                {
                    Identity = identity,
                    LastActiveTime = ctx.Timestamp,
                    ActiveDeviceId = deviceId ?? "unknown"
                });
                LogAuthAction(ctx, "SessionCreated", $"New session created for {user.Username}. Device: {deviceId ?? "unknown"}");
            }
        }

        // 5. Clean up pending verification
        ctx.Db.PendingVerification.Delete(pending);

        // Success - Client state will update via subscriptions to User and AuthSession tables.
        Log.Info($"Auth: Verification successful for Identity {identity}.");
    }

    // ====================== User Management & Other Reducers (Minor Updates) ======================

    [Reducer]
    public static void GetMyProfile(ReducerContext ctx)
    {
        var user = ctx.Db.User.Identity.Find(ctx.Sender);
        if (user == null)
        {
            // User not found, implies not logged in or registered.
            // No action needed server-side, client subscription handles lack of data.
            Log.Warn($"Auth: GetMyProfile called by unauthenticated Identity {ctx.Sender}.");
            return; // Or throw if an error response is preferred client-side
        }
        // Data is available via client subscription to the User table.
        // Log the access attempt if desired.
        LogAuthAction(ctx, "GetMyProfile", $"User {user.Username} requested profile data.");
    }

    [Reducer]
    public static void UpdateProfile(ReducerContext ctx, string? newUsername, string? newEmail) // Allow updating either or both
    {
        Identity identity = ctx.Sender;
        var user = ctx.Db.User.Identity.Find(identity);

        if (user == null)
        {
            throw new Exception("Not authenticated. Please login first.");
        }

        bool changed = false;
        newEmail = newEmail?.ToLowerInvariant().Trim();

        // --- Update Username ---
        if (!string.IsNullOrWhiteSpace(newUsername) && user.Username != newUsername)
        {
            if (!IsValidUsername(newUsername))
            {
                throw new Exception("Invalid username format.");
            }
            // Check if NEW username is taken by ANY OTHER user
            if (ctx.Db.User.Iter().Any(u => u.Identity != identity && u.Username == newUsername))
            {
                throw new Exception($"Username '{newUsername}' is already taken.");
            }
            user.Username = newUsername;
            changed = true;
            LogAuthAction(ctx, "UserProfileUpdated", $"Username changed to {newUsername}.");
        }

        // --- Update Email ---
        if (!string.IsNullOrWhiteSpace(newEmail) && user.Email != newEmail)
        {
            if (!IsValidEmail(newEmail))
            {
                throw new Exception("Invalid email format.");
            }
            // Check if NEW email is taken by ANY OTHER user
            if (ctx.Db.User.Iter().Any(u => u.Identity != identity && u.Email == newEmail))
            {
                throw new Exception($"Email '{newEmail}' is already registered to another account.");
            }
            user.Email = newEmail;
            user.IsEmailVerified = false; // Require re-verification for new email
            changed = true;
            LogAuthAction(ctx, "UserProfileUpdated", $"Email changed to {newEmail}. Verification required.");
            // TODO: Trigger a new verification email process for the new email?
            // Could call RequestVerification internally, or instruct user to do so.
            // For simplicity here, we just mark as unverified.
        }


        if (changed)
        {
            ctx.Db.User.Identity.Update(user);
            Log.Info($"Auth: Profile updated for Identity {identity}.");
        }
        else
        {
            Log.Info($"Auth: UpdateProfile called for Identity {identity}, but no changes were made.");
        }
    }

    // --- Role-Specific Reducers (Ensure correct role checks and Pk usage) ---
    // Example: ListUsersByRole - No changes needed if logic was correct, just ensure Pk.Find is used.

    [Reducer]
    public static void ListUsersByRole(ReducerContext ctx, int roleInt)
    {
        var callingUser = ctx.Db.User.Identity.Find(ctx.Sender); // Use Pk

        if (callingUser == null)
            throw new Exception("Not authenticated.");

        // Example: Use specific roles for authorization
        if (callingUser.Role != UserRole.Auditor && callingUser.Role != UserRole.Auditor)
            throw new Exception("Access denied. Only Auditors or Auditors can list users.");

        if (!Enum.IsDefined(typeof(UserRole), roleInt))
            throw new Exception("Invalid role specified.");

        var role = (UserRole)roleInt;

        // The actual listing happens via client subscription + filtering.
        // This reducer mainly serves as an authorization gate and logs the action.
        var userList = ctx.Db.User.Iter().Where(u => u.Role == role).Select(u => u.Username).ToList(); // Example server-side logic if needed

        LogAuthAction(ctx, "UsersListedByRole", $"User {callingUser.Username} listed users with role {role}. Found: {userList.Count}");
    }

    [Reducer]
    public static void GetAuditLogs(ReducerContext ctx, Timestamp startTime, Timestamp endTime, int limit) // Use Timestamp type
    {
        var callingUser = ctx.Db.User.Identity.Find(ctx.Sender); // Use Pk

        if (callingUser == null)
            throw new Exception("Not authenticated.");

        if (callingUser.Role != UserRole.Auditor) // Only Auditors
            throw new Exception("Access denied. Only auditors can access audit logs.");

        if (limit <= 0 || limit > 1000)
        {
            // Clamp limit or throw error
            limit = Math.Clamp(limit, 1, 1000);
            Log.Warn($"Auth: Audit log limit adjusted to {limit}.");
            // Alternatively: throw new Exception("Limit must be between 1 and 1000.");
        }

        // Actual log retrieval happens via client subscription + filtering on AuditLog table.
        // This reducer logs the access attempt.
        var logsInRange = ctx.Db.AuditLog.Iter()
            .Where(log => log.Timestamp.CompareTo(startTime) >= 0 && log.Timestamp.CompareTo(endTime) <= 0)
            .OrderByDescending(log => log.Timestamp) // Example ordering
            .Take(limit)
            .ToList();


        LogAuthAction(ctx, "AuditLogsAccessed", $"Auditor {callingUser.Username} accessed audit logs from {startTime} to {endTime} (Limit: {limit}). Found: {logsInRange.Count}");
    }

    // Placeholder for Benefits Module interaction (Unchanged)
    [Reducer]
    public static void GetBenefitsByLocation(ReducerContext ctx, double latitude, double longitude, double radiusKm)
    {
        var user = ctx.Db.User.Identity.Find(ctx.Sender);
        if (user == null)
        {
            throw new Exception("Authentication required to search for benefits.");
        }
        LogAuthAction(ctx, "BenefitSearchRequested", $"User {user.Username} searched benefits near ({latitude},{longitude}), radius {radiusKm}km.");
        // TODO: Implement call to BenefitModule.QueryActiveBenefitsNearPoint(ctx, latitude, longitude, radiusKm);
        // Note: Cross-module calls might require specific patterns depending on your setup.
    }
}
}
