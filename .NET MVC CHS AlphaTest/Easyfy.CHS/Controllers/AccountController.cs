﻿using System;
using System.Collections.Generic;
using System.Web.Mvc;
using System.Linq;
using DotNetOpenAuth.AspNet;
using FlexProviders.Membership;
using Easyfy.CHS.Security;
using Microsoft.Web.WebPages.OAuth;
using Easyfy.CHS.Model.Athlete;
using Easyfy.CHS.Model.Account;
using Easyfy.CHS.Models;
using Facebook;
using Raven.Imports.Newtonsoft.Json;
using Easyfy.CHS.Infrastructure;
using Easyfy.CHS.Data.Raven;

namespace Easyfy.CHS.Controllers
{
    [Authorize]
    public class AccountController : RavenController
    {
        private readonly IFlexMembershipProvider _membershipProvider;
        private readonly IFlexOAuthProvider _oAuthProvider;
        private readonly ISecurityEncoder _encoder = new DefaultSecurityEncoder();

        public AccountController(IFlexMembershipProvider membership, IFlexOAuthProvider oauth)
        {
            _membershipProvider = membership;
            _oAuthProvider = oauth;
        }

        //
        // GET: /Account/Login

        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        //
        // POST: /Account/Login

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult Login(LoginModel model, string returnUrl)
        {

            if (ModelState.IsValid && _membershipProvider.Login(model.UserName, model.Password, model.RememberMe))
            {
                Athlete current = (RavenSession.Query<Athlete>().SingleOrDefault(u => u.Username == model.UserName));
                RefreshCurrentUser(current);
                return RedirectToLocal(returnUrl);
            }

            ModelState.AddModelError("", "The user name or password provided is incorrect.");
            return View(model);
        }

        //
        // POST: /Account/LogOff

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogOff()
        {
            _membershipProvider.Logout();
            RefreshCurrentUser(null);
            return RedirectToAction("Index", "Home");
        }

        //
        // GET: /Account/LogOffFacebook
        [AllowAnonymous]
        public ActionResult LogOffFacebook(string accessToken)
        {
            var oauth = new FacebookClient();

            var logoutParameters = new Dictionary<string, object>
            {
                { "access_token", accessToken },
                { "next", "http://localhost:27433" }
            };

            var logoutUrl = oauth.GetLogoutUrl(logoutParameters);

            return Redirect(logoutUrl.ToString());
        }

        //
        // GET: /Account/Register

        [AllowAnonymous]
        public ActionResult Register()
        {
            return View();
        }

        //
        // POST: /Account/Register

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult Register(RegisterModel model)
        {
            if (ModelState.IsValid)
            {
                // Attempt to register the user
                try
                {
                    var user = new Athlete { Username = model.UserName, Password = model.Password };
                    _membershipProvider.CreateAccount(user);
                    return RedirectToAction("Index", "Home");
                }
                catch (FlexMembershipException e)
                {
                    ModelState.AddModelError("", ErrorCodeToString(e.StatusCode));
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // POST: /Account/Disassociate

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Disassociate(string provider, string providerUserId)
        {
            _oAuthProvider.DisassociateOAuthAccount(provider, providerUserId);
            return RedirectToAction("Manage", new { Message = "Complete" });
        }

        //
        // GET: /Account/Manage

        public ActionResult Manage(ManageMessageId? message)
        {
            ViewBag.StatusMessage =
                message == ManageMessageId.ChangePasswordSuccess ? "Your password has been changed."
                : message == ManageMessageId.SetPasswordSuccess ? "Your password has been set."
                : message == ManageMessageId.RemoveLoginSuccess ? "The external login was removed."
                : "";
            ViewBag.HasLocalPassword = _membershipProvider.HasLocalAccount(User.Identity.Name);
            ViewBag.ReturnUrl = Url.Action("Manage");
            return View();
        }

        //
        // POST: /Account/Manage

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Manage(LocalPasswordModel model)
        {
            bool hasLocalAccount = _membershipProvider.HasLocalAccount(User.Identity.Name);
            ViewBag.HasLocalPassword = hasLocalAccount;
            ViewBag.ReturnUrl = Url.Action("Manage");
            if (hasLocalAccount)
            {
                if (ModelState.IsValid)
                {
                    // ChangePassword will throw an exception rather than return false in certain failure scenarios.
                    bool changePasswordSucceeded;
                    try
                    {
                        changePasswordSucceeded = _membershipProvider.ChangePassword(User.Identity.Name, model.OldPassword, model.NewPassword);
                    }
                    catch (Exception)
                    {
                        changePasswordSucceeded = false;
                    }

                    if (changePasswordSucceeded)
                    {
                        return RedirectToAction("Manage", new { Message = ManageMessageId.ChangePasswordSuccess });
                    }
                    else
                    {
                        ModelState.AddModelError("", "The current password is incorrect or the new password is invalid.");
                    }
                }
            }
            else
            {
                // User does not have a local password so remove any validation errors caused by a missing
                // OldPassword field
                ModelState state = ModelState["OldPassword"];
                if (state != null)
                {
                    state.Errors.Clear();
                }

                if (ModelState.IsValid)
                {
                    try
                    {
                        _membershipProvider.SetLocalPassword(User.Identity.Name, model.NewPassword);
                        return RedirectToAction("Manage", new { Message = ManageMessageId.SetPasswordSuccess });
                    }
                    catch (FlexMembershipException e)
                    {
                        ModelState.AddModelError("", e);
                    }
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // GET: /Account/ForgotPassword

        [AllowAnonymous]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        //
        // POST: /Account/ForgotPassword

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult ForgotPassword(ForgotPasswordModel model)
        {
            if (ModelState.IsValid)
            {
                // Attempt to send the user a password email
                string token = _membershipProvider.GeneratePasswordResetToken(model.UserName);
                ViewBag.PasswordResetToken = token;

                // TODO: Send the user an email with the password reset token.
                return View();
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // GET: /Account/ChangeForgottenPassword

        [AllowAnonymous]
        public ActionResult ChangeForgottenPassword()
        {
            // TODO: Pull the password reset token out of the URL and put it into the model.
            return View();
        }

        //
        // POST: /Account/ChangeForgottenPassword

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult ChangeForgottenPassword(ChangeForgottenPasswordModel model)
        {
            if (ModelState.IsValid)
            {
                // Attempt to send the user a password email
                _membershipProvider.ResetPassword(model.ResetPasswordToken, model.NewPassword);

                return View();
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // POST: /Account/ExternalLogin

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult ExternalLogin(string provider, string returnUrl)
        {
            return new ExternalLoginResult(_oAuthProvider, provider, Url.Action("ExternalLoginCallback", new { ReturnUrl = returnUrl }));
        }

        //
        // GET: /Account/ExternalLoginCallback

        [AllowAnonymous]
        public ActionResult ExternalLoginCallback(string returnUrl)
        {
            AuthenticationResult result = _oAuthProvider.VerifyOAuthAuthentication(Url.Action("ExternalLoginCallback", new { ReturnUrl = returnUrl }));
            if (!result.IsSuccessful)
            {
                return RedirectToAction("ExternalLoginFailure");
            }

            if (_oAuthProvider.OAuthLogin(result.Provider, result.ProviderUserId, persistCookie: false))
            {
                
                RefreshCurrentUser(RavenSession.Query<Athlete>().SingleOrDefault(u => u.OAuthAccounts.Any(r => r.Provider == result.Provider && r.ProviderUserId == result.ProviderUserId)));

                return RedirectToLocal(returnUrl);
            }

            if (User.Identity.IsAuthenticated && CurrentUser.Username != null)
            {
                // If the current user is logged in add the new account
                _oAuthProvider.CreateOAuthAccount(result.Provider, result.ProviderUserId, new Athlete()
                {
                    //Name = result.ExtraData["name"],
                    //Gender = result.ExtraData["gender"],
                    //Username = User.Identity.Name
                });

                RefreshCurrentUser(RavenSession.Query<Athlete>().SingleOrDefault(u => u.OAuthAccounts.Any(r => r.Provider == result.Provider && r.ProviderUserId == result.ProviderUserId)));
                
                return RedirectToLocal(returnUrl);
            }
            else
            {
                // User is new, ask for their desired membership name
                string token = result.ExtraData["accesstoken"];
                var athlete = new Athlete();
                var client = new FacebookClient(token);
                dynamic clientResult = client.Get("me?fields=id,email,first_name,last_name,gender,locale,link,username,timezone,location,picture");
                athlete = JsonConvert.DeserializeObject<Athlete>(clientResult.ToString());

                string loginData = _encoder.SerializeOAuthProviderUserId(result.Provider, result.ProviderUserId);
                ViewBag.ProviderDisplayName = _oAuthProvider.GetOAuthClientData(result.Provider).DisplayName;
                ViewBag.ReturnUrl = returnUrl;

                string username = SlugConverter.TitleToSlug(String.Format("{0} {1}", clientResult.first_name, clientResult.last_name));
                int add_to_name = 1;
                while (RavenSession.Query<Athlete>().SingleOrDefault(u => u.Username == username) != null)
                {
                    username = add_to_name > 1 ? username = username.Substring(0, username.Length - 1) + add_to_name.ToString()
                        : username = username + "_" + add_to_name.ToString();

                    add_to_name++;
                }

                return View("ExternalLoginConfirmation", new RegisterExternalLoginModel
                {
                    UserName = username,
                    FirstName = clientResult.first_name,
                    LastName = clientResult.last_name,
                    ProfilePicture = String.Format("http://graph.facebook.com/{0}/picture?type=large", clientResult.username),
                    ExternalLoginData = loginData,
                    AccessToken = token
                });
            }
        }

        //
        // POST: /Account/ExternalLoginConfirmation
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult ExternalLoginConfirmation(RegisterExternalLoginModel model, string returnUrl)
        {
            string provider = null;
            string providerUserId = null;

            if ((User.Identity.IsAuthenticated && CurrentUser.Username != null)|| !_encoder.TryDeserializeOAuthProviderUserId(model.ExternalLoginData, out provider, out providerUserId))
            {
                return RedirectToAction("Manage");
            }

            if (ModelState.IsValid)
            {
                if (!_membershipProvider.HasLocalAccount(model.UserName) || !_membershipProvider.HasLocalAccount(model.Email))
                {
                    _oAuthProvider.CreateOAuthAccount(provider, providerUserId, new Athlete { Username = model.UserName });

                    RefreshCurrentUser(RavenSession.Query<Athlete>().SingleOrDefault(u => u.Username == model.UserName));

                    return RedirectToAction("RegistrationDetails", "Athlete", new
                    {
                        id = model.UserName,
                        provider = provider,
                        providerUserId = providerUserId,
                        accessToken = model.AccessToken,
                        email = model.Email
                    });
                }
                else
                {
                    ModelState.AddModelError("UserName", "User name already exists. Please enter a different user name.");
                }
            }

            ViewBag.ProviderDisplayName = _oAuthProvider.GetOAuthClientData(provider).DisplayName;
            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        //
        // GET: /Account/ExternalLoginFailure

        [AllowAnonymous]
        public ActionResult ExternalLoginFailure()
        {
            return View();
        }

        [AllowAnonymous]
        [ChildActionOnly]
        public ActionResult ExternalLoginsList(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return PartialView("_ExternalLoginsListPartial", _oAuthProvider.RegisteredClientData);
        }

        [ChildActionOnly]
        public ActionResult RemoveExternalLogins()
        {
            var accounts = _oAuthProvider.GetOAuthAccountsFromUserName(User.Identity.Name);
            List<ExternalLogin> externalLogins = new List<ExternalLogin>();
            foreach (OAuthAccount account in accounts)
            {
                AuthenticationClientData clientData = _oAuthProvider.GetOAuthClientData(account.Provider);

                externalLogins.Add(new ExternalLogin
                {
                    Provider = account.Provider,
                    ProviderDisplayName = clientData.DisplayName,
                    ProviderUserId = account.ProviderUserId,
                });
            }

            ViewBag.ShowRemoveButton = externalLogins.Count > 1 || _membershipProvider.HasLocalAccount(User.Identity.Name);
            return PartialView("_RemoveExternalLoginsPartial", externalLogins);
        }

        #region Helpers
        private ActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return RedirectToAction("Index", "Home");
            }
        }

        internal class ExternalLoginResult : ActionResult
        {
            private IFlexOAuthProvider _oAuthProvider;

            public ExternalLoginResult(IFlexOAuthProvider oAuthProvider, string provider, string returnUrl)
            {
                _oAuthProvider = oAuthProvider;
                Provider = provider;
                ReturnUrl = returnUrl;
            }

            public string Provider { get; private set; }
            public string ReturnUrl { get; private set; }

            public override void ExecuteResult(ControllerContext context)
            {
                _oAuthProvider.RequestOAuthAuthentication(Provider, ReturnUrl);
            }
        }

        private static string ErrorCodeToString(FlexMembershipStatus createStatus)
        {
            // See http://go.microsoft.com/fwlink/?LinkID=177550 for
            // a full list of status codes.
            switch (createStatus)
            {
                case FlexMembershipStatus.DuplicateUserName:
                    return "User name already exists. Please enter a different user name.";

                case FlexMembershipStatus.DuplicateEmail:
                    return "A user name for that e-mail address already exists. Please enter a different e-mail address.";

                case FlexMembershipStatus.InvalidPassword:
                    return "The password provided is invalid. Please enter a valid password value.";

                case FlexMembershipStatus.InvalidEmail:
                    return "The e-mail address provided is invalid. Please check the value and try again.";

                case FlexMembershipStatus.InvalidAnswer:
                    return "The password retrieval answer provided is invalid. Please check the value and try again.";

                case FlexMembershipStatus.InvalidQuestion:
                    return "The password retrieval question provided is invalid. Please check the value and try again.";

                case FlexMembershipStatus.InvalidUserName:
                    return "The user name provided is invalid. Please check the value and try again.";

                case FlexMembershipStatus.ProviderError:
                    return "The authentication provider returned an error. Please verify your entry and try again. If the problem persists, please contact your system administrator.";

                case FlexMembershipStatus.UserRejected:
                    return "The user creation request has been canceled. Please verify your entry and try again. If the problem persists, please contact your system administrator.";

                default:
                    return "An unknown error occurred. Please verify your entry and try again. If the problem persists, please contact your system administrator.";
            }
        }
        #endregion
    }
}
