# Authentication System - McStud Desktop

This document explains how to use and configure the authentication system for McStud Desktop.

## Overview

The application now includes a complete authentication system with:
- **Login Page** - Users must authenticate before accessing the estimating tool
- **Self-Registration** - Clients can create their own accounts
- **Admin Approval** - New registrations require admin approval
- **Password Reset** - Email notifications for password reset requests
- **Remember Me** - Option to save login credentials locally
- **Admin Panel** - Manage users and configure email settings

## First Time Setup

### 1. Default Admin Account

When you run the app for the first time, a default admin account is automatically created:
- **Username:** `admin`
- **Password:** `admin123`

**IMPORTANT:** Change this password immediately after first login!

### 2. Configure Email Notifications

To receive email notifications for password resets and new registrations:

1. Log in with the admin account
2. Click "Admin Panel" button
3. Scroll to "Email Notification Settings"
4. Enter your SMTP settings:
   - **SMTP Server:** `smtp.gmail.com` (for Gmail)
   - **SMTP Port:** `587`
   - **Admin Email:** Your email address (where notifications will be sent)
   - **Sender Email:** The Gmail address used to send emails
   - **Sender Password:** Gmail App Password (see below)

5. Click "Save Email Settings"

### 3. Gmail App Password Setup

For Gmail, you need to create an App Password:

1. Go to your Google Account settings
2. Navigate to **Security**
3. Enable **2-Step Verification** if not already enabled
4. Go to **2-Step Verification > App Passwords**
5. Generate a new app password for "Mail"
6. Copy this 16-character password
7. Use this password in the "Sender Password" field

**Note:** Regular Gmail passwords won't work. You must use an App Password.

## User Registration Flow

### For Clients (End Users)

1. Open the application
2. Click "Create Account" on the login page
3. Enter a username (minimum 3 characters)
4. Enter a password (minimum 6 characters)
5. Confirm password
6. Click "Register"
7. Wait for admin approval

**Note:** You won't be able to login until the admin approves your account.

### For Admins

When a new user registers:
1. You'll receive an email notification (if email is configured)
2. Open the Admin Panel in the application
3. Review pending users in the "Pending User Approvals" section
4. Click "Approve" to activate the account, or "Reject" to deny

## Password Reset Flow

### For Clients

1. On the login page, enter your username
2. Click "Forgot Password?"
3. The admin will be notified via email
4. Wait for the admin to reset your password

### For Admins

Currently, password resets must be done manually:
1. You'll receive an email notification when a user requests a reset
2. Contact the user directly to verify their identity
3. Use the authentication service to reset their password (feature coming soon)

**Future Enhancement:** A password reset feature will be added to the Admin Panel.

## Security Features

### Password Security
- Passwords are hashed using SHA-256 before storage
- No plaintext passwords are stored in the database

### Data Encryption
- User data is stored in an encrypted file (`users.dat`)
- AES-128 encryption is used to protect the data at rest

### Remember Me
- Credentials are stored locally in base64-encoded format (`remember.dat`)
- Only stored when the user explicitly checks "Remember me"
- Cleared when user logs out without "Remember me" checked

## File Storage

The application creates these files in the application directory:

- **users.dat** - Encrypted user credentials
- **email_settings.json** - Email notification settings (plaintext)
- **remember.dat** - Saved login credentials (base64 encoded)

**IMPORTANT:** Do NOT share or delete these files. Back them up regularly.

## Admin Panel Features

### User Management
- View all pending user registrations
- Approve or reject new accounts
- See registration dates and usernames

### Email Settings
- Configure SMTP server and port
- Set admin notification email
- Configure sender credentials
- Test email configuration

## Troubleshooting

### Email Notifications Not Working

1. **Check email settings:**
   - Verify SMTP server and port are correct
   - Ensure you're using a Gmail App Password, not your regular password
   - Confirm sender email is valid

2. **Gmail-specific issues:**
   - Make sure 2-Step Verification is enabled
   - Verify the App Password is correct (16 characters, no spaces)
   - Check that "Less secure app access" is NOT required (App Passwords bypass this)

3. **Test the connection:**
   - Try sending a password reset notification
   - Check the Debug output in Visual Studio for error messages

### Can't Login After Registration

- Your account needs admin approval first
- Ask the admin to approve your account in the Admin Panel
- Check the "Pending User Approvals" section

### Forgot Admin Password

If you lose access to the admin account:

1. Close the application
2. Delete the `users.dat` file
3. Restart the application
4. A new default admin account will be created
5. **WARNING:** This will delete ALL user accounts!

## Development Notes

### Architecture

- **AuthenticationService** - Handles user management, password hashing, and encryption
- **EmailNotificationService** - Sends email notifications for events
- **LoginPage** - User login interface with remember me functionality
- **RegisterPage** - User registration interface
- **AdminPanel** - Admin tools for user management and configuration
- **MainWindow** - Navigation controller between pages

### Adding Features

To extend the authentication system:

1. **Add password reset in Admin Panel:**
   - Add a "Reset Password" button in the pending users list
   - Call `AuthenticationService.ResetPassword(username, newPassword)`

2. **Add user roles:**
   - Extend `UserAccount` class with a `Role` property
   - Implement role-based access control in `MainWindow`

3. **Add email verification:**
   - Generate verification tokens on registration
   - Send verification email with token
   - Add verification page to confirm token

## Security Recommendations

1. **Change default admin password immediately**
2. **Use strong passwords** (12+ characters, mixed case, numbers, symbols)
3. **Enable 2FA** on your Gmail account for email notifications
4. **Backup** `users.dat` and `email_settings.json` regularly
5. **Never share** your email settings or App Password
6. **Use environment variables** for sensitive settings in production

## Support

For issues or questions about the authentication system, check the Debug output or contact the development team.
