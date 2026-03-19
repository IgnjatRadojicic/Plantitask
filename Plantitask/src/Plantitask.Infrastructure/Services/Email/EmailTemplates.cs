namespace Plantitask.Infrastructure.Services.Email
{
    public static class EmailTemplates
    {
        private static string BaseTemplate(string content)
        {
            var logoUrl = "https://raw.githubusercontent.com/IgnjatRadojicic/Plantitask/main/Plantitask/logo.png";

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            margin: 0;
            padding: 0;
            background-color: #f4f7f0;
        }}
        .container {{
            max-width: 600px;
            margin: 0 auto;
            background-color: #ffffff;
            border-radius: 8px;
            overflow: hidden;
            margin-top: 20px;
            margin-bottom: 20px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        .header {{
            background-color: #4a7c2e;
            color: white;
            padding: 28px 24px;
            text-align: center;
        }}
        .header img {{
            display: block;
            margin: 0 auto 12px auto;
            border-radius: 50%;
            background-color: #ffffff;
            padding: 6px;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
            font-weight: 700;
            letter-spacing: 0.5px;
        }}
        .body {{
            padding: 32px 24px;
            color: #333333;
            line-height: 1.6;
        }}
        .body h2 {{
            color: #4a7c2e;
            margin-top: 0;
        }}
        .button {{
            display: inline-block;
            background-color: #4a7c2e;
            color: white;
            text-decoration: none;
            padding: 12px 28px;
            border-radius: 6px;
            margin: 16px 0;
            font-weight: 600;
            font-size: 14px;
        }}
        .highlight {{
            background-color: #f2f7e9;
            padding: 16px;
            border-radius: 6px;
            border-left: 4px solid #7cb342;
            margin: 12px 0;
        }}
        .code-box {{
            text-align: center;
            font-size: 24px;
            letter-spacing: 4px;
            background-color: #f2f7e9;
            padding: 16px;
            border-radius: 6px;
            font-weight: bold;
            color: #4a7c2e;
        }}
        .footer {{
            padding: 16px 24px;
            text-align: center;
            color: #999999;
            font-size: 12px;
            border-top: 1px solid #eeeeee;
        }}
        .footer p {{
            margin: 4px 0;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <!--[if mso]>
        <table role='presentation' width='600' align='center' cellpadding='0' cellspacing='0' border='0'>
        <tr><td>
        <![endif]-->

        <div class='header'>
            <!--[if mso]>
            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
            <tr><td align='center'>
            <![endif]-->
            <img src='{logoUrl}' alt='Plantitask Logo' width='100' height='100'
                 style='display:block; margin:0 auto 12px auto; border-radius:50%; background-color:#ffffff; padding:6px; width:100px; height:100px;' />
            <!--[if mso]>
            </td></tr>
            <tr><td align='center'>
            <![endif]-->
            <h1 style='margin:0; font-size:24px; font-weight:700; letter-spacing:0.5px; color:#ffffff;'>Plantitask</h1>
            <!--[if mso]>
            </td></tr>
            </table>
            <![endif]-->
        </div>

        <div class='body'>
            {content}
        </div>

        <div class='footer'>
            <p style='margin:4px 0; color:#999999; font-size:12px;'>This is an automated message from Plantitask. Please do not reply to this email.</p>
            <p style='margin:4px 0; color:#bbbbbb; font-size:11px;'>&copy; {DateTime.UtcNow.Year} Plantitask. All rights reserved.</p>
        </div>

        <!--[if mso]>
        </td></tr>
        </table>
        <![endif]-->
    </div>
</body>
</html>";
        }

        public static string Welcome(string firstName)
        {
            return BaseTemplate($@"
                <h2 style='color:#4a7c2e; margin-top:0;'>Welcome to Plantitask, {firstName}!</h2>
                <p>Your account has been created successfully. Your journey starts with a seed — create your first group and watch your tree grow as your team completes tasks.</p>
                <p>Get started by creating your first group or joining an existing one.</p>");
        }

        public static string PasswordReset(string userName, string resetLink)
        {
            return BaseTemplate($@"
                <h2 style='color:#4a7c2e; margin-top:0;'>Password Reset Request</h2>
                <p>Hello {userName},</p>
                <p>You requested to reset your password. Click the button below to set a new password:</p>
                <p style='text-align: center;'>
                    <a href='{resetLink}' class='button'
                       style='display:inline-block; background-color:#4a7c2e; color:white; text-decoration:none; padding:12px 28px; border-radius:6px; font-weight:600; font-size:14px;'>
                        Reset Password
                    </a>
                </p>
                <p>This link expires in 1 hour.</p>
                <p>If you didn't request this, you can safely ignore this email. Your password will remain unchanged.</p>");
        }

        public static string TaskAssignment(string userName, string taskTitle, string groupName, string assignedBy)
        {
            return BaseTemplate($@"
                <h2 style='color:#4a7c2e; margin-top:0;'>New Task Assigned</h2>
                <p>Hello {userName},</p>
                <p><strong>{assignedBy}</strong> assigned you a new task in <strong>{groupName}</strong>:</p>
                <div class='highlight' style='background-color:#f2f7e9; padding:16px; border-radius:6px; border-left:4px solid #7cb342; margin:12px 0;'>
                    {taskTitle}
                </div>
                <p>Log in to view the details and start working on it.</p>");
        }

        public static string GroupInvitation(string inviterName, string groupName, string groupCode)
        {
            return BaseTemplate($@"
                <h2 style='color:#4a7c2e; margin-top:0;'>You've Been Invited!</h2>
                <p><strong>{inviterName}</strong> invited you to join <strong>{groupName}</strong> on Plantitask.</p>
                <p>Use this code to join:</p>
                <div class='code-box' style='text-align:center; font-size:24px; letter-spacing:4px; background-color:#f2f7e9; padding:16px; border-radius:6px; font-weight:bold; color:#4a7c2e;'>
                    {groupCode}
                </div>
                <p>Log in to your account and enter this code to join the group.</p>");
        }

        public static string TaskComment(string userName, string commenterName, string taskTitle, string commentText)
        {
            return BaseTemplate($@"
                <h2 style='color:#4a7c2e; margin-top:0;'>New Comment on Your Task</h2>
                <p>Hello {userName},</p>
                <p><strong>{commenterName}</strong> commented on <strong>{taskTitle}</strong>:</p>
                <div class='highlight' style='background-color:#f2f7e9; padding:16px; border-radius:6px; border-left:4px solid #7cb342; margin:12px 0; font-style:italic;'>
                    {commentText}
                </div>");
        }

        public static string TaskDueSoon(string userName, string taskTitle, DateTime dueDate)
        {
            return BaseTemplate($@"
                <h2 style='color:#4a7c2e; margin-top:0;'>Task Due Soon</h2>
                <p>Hello {userName},</p>
                <p>Your task is due on <strong>{dueDate:MMMM dd, yyyy 'at' h:mm tt} UTC</strong>:</p>
                <div class='highlight' style='background-color:#f2f7e9; padding:16px; border-radius:6px; border-left:4px solid #7cb342; margin:12px 0;'>
                    {taskTitle}
                </div>
                <p>Log in to check the details and make sure it's on track.</p>");
        }

        public static string TaskOverdue(string userName, string taskTitle, int daysOverdue)
        {
            var overdueText = daysOverdue == 0
                ? "is now overdue"
                : $"is overdue by {daysOverdue} day(s)";

            return BaseTemplate($@"
                <h2 style='color:#4a7c2e; margin-top:0;'>Task Overdue</h2>
                <p>Hello {userName},</p>
                <p>Your task {overdueText}:</p>
                <div class='highlight' style='background-color:#f2f7e9; padding:16px; border-radius:6px; border-left:4px solid #7cb342; margin:12px 0;'>
                    {taskTitle}
                </div>
                <p>Log in to update the status or reach out to your team.</p>");
        }

        public static string EmailVerification(string userName, string code)
        {
            return BaseTemplate($@"
        <h2 style='color:#4a7c2e; margin-top:0;'>Verify Your Email</h2>
        <p>Hello {userName},</p>
        <p>Your verification code is:</p>
        <div class='code-box' style='text-align:center; font-size:32px; letter-spacing:8px; background-color:#f2f7e9; padding:20px; border-radius:6px; font-weight:bold; color:#4a7c2e;'>
            {code}
        </div>
        <p>This code expires in 15 minutes.</p>
        <p>If you didn't create an account, you can safely ignore this email.</p>");
        }
    }
}