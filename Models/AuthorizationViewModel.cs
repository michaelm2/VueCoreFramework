﻿namespace MVCCoreVue.Models
{
    /// <summary>
    /// A ViewModel used to transfer information during user account authorization tasks.
    /// </summary>
    public class AuthorizationViewModel
    {
        /// <summary>
        /// A value indicating that the user is authorized for the requested action.
        /// </summary>
        public const string Authorized = "authorized";

        /// <summary>
        /// A value indicating that the user is not authorized for the requested action.
        /// </summary>
        public const string Unauthorized = "unauthorized";

        /// <summary>
        /// The username of the user account.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// The email address of the user account.
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// A JWT bearer token.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// A value indicating whether the user is authorized for the requested action or not.
        /// </summary>
        public string Authorization { get; set; }
    }
}
