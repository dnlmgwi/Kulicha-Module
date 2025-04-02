using System.Diagnostics;
using SpacetimeDB;
using System.Text.RegularExpressions;

namespace StdbModule
{
    // ====================== Database Table Definitions ======================

    /// <summary>
    /// Table that stores user accounts and their roles
    /// </summary>
    [Table]
    public partial class User
    {
        [PrimaryKey]
        public Identity Identity;
        public string? Username;
        public string? Email;
        public UserRole Role { get; init; }
        public bool IsEmailVerified;
        public Timestamp RegisteredAt;
    }

    /// <summary>
    /// Enum defining the three roles in our system
    /// </summary>
    public enum UserRole
    {
        Beneficiary = 0,
        Benefactor = 1,
        Auditor = 2
    }

    /// <summary>
    /// Table for storing pending registrations that need email verification
    /// </summary>
    [Table]
    public partial class PendingRegistration
    {
        [PrimaryKey]
        public Identity Identity;

        public string? Username;
        public string? Email;
        public UserRole Role { get; init; }
        public string? VerificationCode;

        public Timestamp ExpiresAt;
    }

    /// <summary>
    /// Table for authentication sessions
    /// </summary>
    [Table]
    public partial class AuthSession
    {
        [PrimaryKey]
        public Identity Identity;
        public Timestamp LastActiveTime;
        public string? ActiveDeviceId;
    }

    /// <summary>
    /// Audit log for tracking important system events
    /// </summary>
    [Table]
    public partial class AuditLog
    {
        [AutoInc]
        [PrimaryKey]
        public long Field;
        public Identity Identity; // User who performed the action
        public string? Action; // Type of action performed
        public string? Details; // Additional context
        public Timestamp Timestamp;
    }

    // ====================== Auth Module Implementation ======================

    public static partial class AuthModule
    {
        // ====================== Helper Constants & Methods ======================

        private const string EmailPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
        private const string UsernamePattern = @"^[a-zA-Z0-9_-]{3,20}$";
        private static readonly Random Random = new Random();

        /// <summary>
        /// Validates an email format
        /// </summary>
        private static bool IsValidEmail(string email)
        {
            return !string.IsNullOrWhiteSpace(email) && Regex.IsMatch(email,EmailPattern);
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

            var existingUser = ctx.Db.User.Identity.Find(identity);

            if (existingUser != null)
            {
                // Update the auth session for existing users
                var authSession = new AuthSession
                {
                    Identity = identity,
                    LastActiveTime = ctx.Timestamp,
                    ActiveDeviceId = "web" //TODO: Get Device ID
                };

                ctx.Db.AuthSession.Identity.Update(authSession);

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
                Log.Error( "Invalid username. Username must be 3-20 characters and contain only letters, numbers, underscore or hyphen.");

            if (!IsValidEmail(email))
                Log.Error( "Invalid email format.");

            if (!Enum.IsDefined(typeof(UserRole), roleInt))
                Log.Error( "Invalid role specified.");

            var role = (UserRole)roleInt;

            // Create a verification code
            string verificationCode = GenerateVerificationCode();

            // Create a TimeDuration object for one day
            var oneDay = new TimeDuration { Microseconds = 86400000000 };

            // Store pending registration
            var pendingReg = new PendingRegistration
            {
                Identity = identity,
                Username = username,
                Email = email,
                Role = role,
                VerificationCode = verificationCode,
                ExpiresAt = ctx.Timestamp + oneDay // Expires in 24 hours
            };

            ctx.Db.PendingRegistration.Insert(pendingReg);

            LogInfo(ctx, identity, "UserRegistrationRequested",
                $"User registration requested with username {username}, email {email}, role {role}");

            // In a real app, you would send an email with the verification code
            // For now, we just return it to the user
            Log.Info($"Registration successful! Verification code: {verificationCode}");
        }

        /// <summary>
        /// Verify email with verification code to complete registration
        /// </summary>
        [Reducer]
        public static void VerifyEmail(ReducerContext ctx, string verificationCode)
        {
            Identity identity = ctx.Sender;

            // Get pending registration
            var pendingReg = ctx.Db.PendingRegistration.Identity.Find(identity);

            if (pendingReg == null)
                Log.Error("No pending registration found."); //TODO Notify User

            // Check if code has expired
            if (pendingReg?.ExpiresAt < ctx.Timestamp)
            {
                ctx.Db.PendingRegistration.Delete(pendingReg);
                Log.Error("Verification code has expired. Please register again."); //TODO Notify User
            }

            // Verify the code
            if (pendingReg?.VerificationCode != verificationCode)
                Log.Error("Invalid verification code."); //TODO Notify User

            // Create the user
            var user = new User
            {
                Identity = identity,
                Username = pendingReg!.Username,
                Email = pendingReg.Email,
                Role = pendingReg.Role,
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
            ctx.Db.PendingRegistration.Delete(pendingReg);

            LogInfo(ctx, identity, "UserRegistrationCompleted",
                $"User registration completed for {user.Username} with role {user.Role}");

            Log.Info( $"Email verification successful! You are now registered as a {user.Role}."); //TODO Notify User
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


            // Validate email
            if (!IsValidEmail(email))
               Log.Warn("Invalid email format.");

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
            Log.Info( "Profile updated successfully. Please verify your new email.");
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
                Log.Warn( "Not authenticated. Please register first.");

            if (user!.Role != UserRole.Auditor)
                Log.Error( "Access denied. Only auditors can list users.");


            // Validate role
            if (!Enum.IsDefined(typeof(UserRole), roleInt))
                Log.Error( "Invalid role specified.");

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
               Log.Error( "Access denied. Only auditors can access audit logs.");

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
    }
}
