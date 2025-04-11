// using System.Net.Http.Headers;
// using System.Text;
// using System.Text.Json;

// namespace StdbModule.Services;

// using SpacetimeDB;
// public class EmailService
// {
//     private readonly HttpClient _httpClient;
//     private const string ResendApiEndpoint = "https://api.resend.com/emails";

//     public EmailService()
//     {
//         _httpClient = new HttpClient();
//         var apiKey = "re_jb7Ede2a_CcaWitzTd9z1UyjXZJyjFFkE";
//         _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
//     }

//     public async Task SendVerificationEmailAsync(string email, string verificationCode, bool isRegistration = true)
//     {
//         try
//         {
//             var subject = isRegistration ? "Verify Your Email - Kulicha" : "Login Verification Code - Kulicha";
//             var htmlContent = GenerateEmailContent(email, verificationCode, isRegistration);

//             var emailRequest = new
//             {
//                 from = "noreply@kulicha.app",
//                 to = email,
//                 subject = subject,
//                 html = htmlContent
//             };

//             var json = JsonSerializer.Serialize(emailRequest);
//             var content = new StringContent(json, Encoding.UTF8, "application/json");

//             var response = await _httpClient.PostAsync(ResendApiEndpoint, content);

//             if (!response.IsSuccessStatusCode)
//             {
//                 var errorContent = await response.Content.ReadAsStringAsync();
//                 Log.Error($"Failed to send verification email. Status: {response.StatusCode}, Error: {errorContent}");
//                 throw new Exception($"Failed to send verification email: {errorContent}");
//             }

//             Log.Info("Successfully sent verification email to {Email}", email);
//         }
//         catch (Exception ex)
//         {
//             Log.Error($"Error sending verification email to {email}: {ex.Message}");
//             throw;
//         }
//     }

//     private string GenerateEmailContent(string email, string verificationCode, bool isRegistration)
//     {
//         string actionType = isRegistration ? "registration" : "login";
//         string greeting = isRegistration ? "Welcome to Kulicha!" : "Login Verification";
//         string instructions = isRegistration 
//             ? "Thank you for registering with Kulicha. Please use the verification code below to complete your registration:" 
//             : "Please use the verification code below to complete your login:";

//         return $@"<!DOCTYPE html>
// <html>
// <head>
//     <meta charset="UTF-8">
//     <meta name="viewport" content="width=device-width, initial-scale=1.0">
//     <title>{greeting}</title>
//     <style>
//         body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
//         .container {{ border: 1px solid #ddd; border-radius: 5px; padding: 20px; }}
//         .header {{ text-align: center; padding-bottom: 20px; border-bottom: 1px solid #eee; }}
//         .code {{ font-size: 24px; font-weight: bold; text-align: center; padding: 15px; margin: 20px 0; background-color: #f5f5f5; border-radius: 4px; letter-spacing: 5px; }}
//         .footer {{ margin-top: 30px; font-size: 12px; color: #777; text-align: center; }}
//     </style>
// </head>
// <body>
//     <div class="container">
//         <div class="header">
//             <h2>{greeting}</h2>
//         </div>
//         <p>Hello,</p>
//         <p>{instructions}</p>
//         <div class="code">{verificationCode}</div>
//         <p>This code will expire in 15 minutes for security reasons.</p>
//         <p>If you did not request this {actionType}, please ignore this email or contact support if you have concerns.</p>
//         <div class="footer">
//             <p>&copy; {DateTime.Now.Year} Kulicha. All rights reserved.</p>
//         </div>
//     </div>
// </body>
// </html>";
//     }
// }
