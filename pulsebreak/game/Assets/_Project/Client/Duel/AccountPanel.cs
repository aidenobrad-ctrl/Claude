using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Pulsebreak.Client.Online;

namespace Pulsebreak.Client.Duel
{
    /// <summary>
    /// The account part of the main menu: a line saying who you are signed in as, and the
    /// forms to sign in, create an account, verify an email address and reset a password.
    ///
    /// The forms check nothing themselves beyond "is something typed": every rule (what makes
    /// a valid username, how long a password must be) is the backend's, and its messages are
    /// shown next to the field they are about.
    /// </summary>
    public sealed class AccountPanel
    {
        enum Form { None, SignIn, Register, Verify, Forgot, Reset }

        static readonly Color Text = new Color32(240, 244, 250, 255);
        static readonly Color Dim = new Color(0.94f, 0.96f, 0.98f, 0.62f);
        static readonly Color Accent = new Color32(38, 216, 255, 255);
        static readonly Color Good = new Color32(96, 232, 140, 255);
        static readonly Color Bad = new Color32(255, 96, 84, 255);

        readonly GameServices _services;
        readonly VisualElement _mainCard;
        readonly VisualElement _formCard;
        readonly Label _summary, _summaryDetail;
        readonly Button _signInButton, _registerButton, _verifyButton, _signOutButton;

        readonly Label _formTitle, _formHint, _formStatus;
        readonly TextField _email, _login, _username, _password, _code;
        readonly Button _submit, _secondary, _back, _forgotLink;
        Form _form;
        bool _busy;

        /// <summary>The summary went from signed out to signed in or back. The menu hides its name box while signed in.</summary>
        public event Action Changed;

        AccountClient Account => _services.Account;

        /// <param name="summaryHost">Where the "signed in as" line goes, inside the main menu card.</param>
        /// <param name="overlay">The menu's backdrop. The forms are a second card on it, shown in place of the main one.</param>
        public AccountPanel(GameServices services, VisualElement mainCard, VisualElement summaryHost, VisualElement overlay)
        {
            _services = services;
            _mainCard = mainCard;

            // ---- the summary line in the main menu
            var box = new VisualElement();
            box.style.marginBottom = 16;
            box.style.paddingLeft = box.style.paddingRight = 12;
            box.style.paddingTop = box.style.paddingBottom = 10;
            box.style.backgroundColor = new Color(0.11f, 0.13f, 0.18f, 1f);
            Round(box, 6);
            summaryHost.Add(box);

            _summary = Label("", 16, Text, bold: true);
            _summaryDetail = Label("", 13, Dim);
            _summaryDetail.style.whiteSpace = WhiteSpace.Normal;
            _summaryDetail.style.marginTop = 2;
            box.Add(_summary);
            box.Add(_summaryDetail);

            var buttons = Row();
            buttons.style.marginTop = 8;
            _signInButton = SmallButton("Sign in", () => Show(Form.SignIn));
            _registerButton = SmallButton("Create account", () => Show(Form.Register));
            _verifyButton = SmallButton("Verify email", () => Show(Form.Verify));
            _signOutButton = SmallButton("Sign out", () => _ = Run(async () =>
            {
                await Account.SignOutAsync();
                return null;
            }));
            buttons.Add(_signInButton);
            buttons.Add(_registerButton);
            buttons.Add(_verifyButton);
            buttons.Add(_signOutButton);
            box.Add(buttons);

            // ---- the form card
            _formCard = new VisualElement();
            _formCard.AddToClassList("pause-card");
            _formCard.style.width = 520;
            _formCard.style.paddingLeft = _formCard.style.paddingRight = 32;
            _formCard.style.paddingTop = _formCard.style.paddingBottom = 28;
            _formCard.style.backgroundColor = new Color(0.07f, 0.09f, 0.125f, 1f);
            _formCard.style.display = DisplayStyle.None;
            overlay.Add(_formCard);

            _formTitle = Label("", 26, Text, bold: true);
            _formTitle.style.letterSpacing = 4;
            _formTitle.style.marginBottom = 6;
            _formHint = Label("", 13, Dim);
            _formHint.style.whiteSpace = WhiteSpace.Normal;
            _formHint.style.marginBottom = 12;
            _formCard.Add(_formTitle);
            _formCard.Add(_formHint);

            _email = Field("Email", maxLength: 254);
            _login = Field("Email or username", maxLength: 254);
            _username = Field("Username", maxLength: 20);
            _code = Field("Code", maxLength: 16);
            _password = Field("Password", maxLength: 128);
            _password.isPasswordField = true;
            foreach (TextField field in new[] { _email, _login, _username, _code, _password })
            {
                _formCard.Add(field);
                // Enter submits, wherever the cursor is.
                field.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit();
                }, TrickleDown.TrickleDown);
            }

            _forgotLink = SmallButton("Forgot your password?", () => Show(Form.Forgot));
            _forgotLink.style.alignSelf = Align.FlexStart;
            _forgotLink.style.backgroundColor = Color.clear;
            _forgotLink.style.color = Accent;
            _forgotLink.style.marginLeft = 0;
            _formCard.Add(_forgotLink);

            _formStatus = Label("", 14, Bad);
            _formStatus.style.whiteSpace = WhiteSpace.Normal;
            _formStatus.style.marginTop = 10;
            _formStatus.style.minHeight = 20;
            _formCard.Add(_formStatus);

            var actions = Row();
            actions.style.justifyContent = Justify.FlexEnd;
            actions.style.marginTop = 16;
            _back = MenuButton("Back", false, () => Show(Form.None));
            _secondary = MenuButton("", false, SecondaryAction);
            _submit = MenuButton("", true, Submit);
            actions.Add(_back);
            actions.Add(_secondary);
            actions.Add(_submit);
            _formCard.Add(actions);

            Account.Changed += Refresh;
            services.Notice += message =>
            {
                Refresh();
                _summaryDetail.text = message;
                _summaryDetail.style.color = Bad;
            };
            Refresh();
        }

        public bool FormOpen => _form != Form.None;

        public void Dispose()
        {
            Account.Changed -= Refresh;
        }

        // ------------------------------------------------------------------ the summary

        /// <summary>Redraws the summary line for whoever is signed in now.</summary>
        public void Refresh()
        {
            AccountData account = Account.Account;
            _summaryDetail.style.color = Dim;

            if (account == null && _services.Resuming)
            {
                _summary.text = "Signing in...";
                _summaryDetail.text = "";
                SetVisible(_signInButton, false);
                SetVisible(_registerButton, false);
                SetVisible(_verifyButton, false);
                SetVisible(_signOutButton, false);
            }
            else if (account == null)
            {
                _summary.text = "Not signed in";
                _summaryDetail.text = "You can play without an account. Sign in to keep your loadouts and settings on your account, and to join servers that need one.";
                SetVisible(_signInButton, true);
                SetVisible(_registerButton, true);
                SetVisible(_verifyButton, false);
                SetVisible(_signOutButton, false);
            }
            else
            {
                _summary.text = "Signed in as " + account.username;
                _summaryDetail.text = (account.emailVerified ? "Email verified. " : "Email not verified: verify it to play ranked. ") +
                                      _services.LoadoutSync.Status + ".";
                SetVisible(_signInButton, false);
                SetVisible(_registerButton, false);
                SetVisible(_verifyButton, !account.emailVerified);
                SetVisible(_signOutButton, true);
            }

            // A form that no longer makes sense closes itself (for example, signed in from another form).
            if ((_form == Form.Verify && (account == null || account.emailVerified)) ||
                ((_form == Form.SignIn || _form == Form.Register) && account != null))
                Show(account != null && !account.emailVerified && _form == Form.Register ? Form.Verify : Form.None);

            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ the forms

        void Show(Form form)
        {
            _form = form;
            _formStatus.text = "";
            _mainCard.style.display = form == Form.None ? DisplayStyle.Flex : DisplayStyle.None;
            _formCard.style.display = form == Form.None ? DisplayStyle.None : DisplayStyle.Flex;
            if (form == Form.None) return;

            SetVisible(_email, form == Form.Register || form == Form.Forgot || form == Form.Reset);
            SetVisible(_login, form == Form.SignIn);
            SetVisible(_username, form == Form.Register);
            SetVisible(_code, form == Form.Verify || form == Form.Reset);
            SetVisible(_password, form == Form.SignIn || form == Form.Register || form == Form.Reset);
            SetVisible(_forgotLink, form == Form.SignIn);
            _password.label = form == Form.Reset ? "New password" : "Password";
            _password.value = "";
            _code.value = "";
            _email.SetEnabled(form != Form.Reset);

            switch (form)
            {
                case Form.SignIn:
                    _formTitle.text = "SIGN IN";
                    _formHint.text = "With your email address or your username.";
                    _submit.text = "Sign in";
                    _secondary.text = "Create account";
                    break;
                case Form.Register:
                    _formTitle.text = "CREATE ACCOUNT";
                    _formHint.text = "A username is 3 to 20 letters, numbers or underscores. A password needs at least 10 characters; a few words strung together is a good one.";
                    _submit.text = "Create account";
                    _secondary.text = "I have an account";
                    break;
                case Form.Verify:
                    _formTitle.text = "VERIFY EMAIL";
                    _formHint.text = "We sent a 6-digit code to " + (Account.Account?.email ?? "your email address") + ". Type it here, or open the link in the email.";
                    _submit.text = "Verify";
                    _secondary.text = "Send a new code";
                    break;
                case Form.Forgot:
                    _formTitle.text = "RESET PASSWORD";
                    _formHint.text = "We will email a code to this address if it has an account.";
                    _submit.text = "Email me a code";
                    _secondary.text = "I have a code";
                    break;
                case Form.Reset:
                    _formTitle.text = "NEW PASSWORD";
                    _formHint.text = "Type the code from the email and choose a new password. Every device you are signed in on will be signed out.";
                    _submit.text = "Set password";
                    _secondary.text = "Send another code";
                    break;
            }
            SetVisible(_secondary, true);
            FocusFirst();
        }

        void FocusFirst()
        {
            foreach (TextField field in new[] { _login, _email, _username, _code, _password })
            {
                if (field.style.display.value == DisplayStyle.None || !field.enabledSelf) continue;
                if (field == _email && _form == Form.Reset) continue;
                field.Focus();
                return;
            }
        }

        void Submit()
        {
            if (_busy) return;
            switch (_form)
            {
                case Form.SignIn:
                    if (Required(_login, _password)) _ = Run(async () => await Account.SignInAsync(_login.value.Trim(), _password.value));
                    break;
                case Form.Register:
                    if (Required(_email, _username, _password))
                        _ = Run(async () => await Account.RegisterAsync(_email.value.Trim(), _username.value.Trim(), _password.value), andThen: Form.Verify);
                    break;
                case Form.Verify:
                    if (Required(_code)) _ = Run(async () => await Account.VerifyEmailAsync(_code.value), done: "Verified. Ranked play is open to you.");
                    break;
                case Form.Forgot:
                    if (Required(_email)) _ = Run(async () => await Account.ForgotPasswordAsync(_email.value.Trim()), andThen: Form.Reset);
                    break;
                case Form.Reset:
                    if (Required(_code, _password))
                        _ = Run(async () => await Account.ResetPasswordAsync(_email.value.Trim(), _code.value, _password.value), andThen: Form.SignIn,
                            done: "Password changed. Sign in with the new one.");
                    break;
            }
        }

        void SecondaryAction()
        {
            if (_busy) return;
            switch (_form)
            {
                case Form.SignIn: Show(Form.Register); break;
                case Form.Register: Show(Form.SignIn); break;
                case Form.Forgot: Show(Form.Reset); break;
                case Form.Verify:
                    _ = Run(async () => await Account.ResendVerificationAsync(), done: "A new code is on its way. Only the newest code works.");
                    break;
                case Form.Reset:
                    if (Required(_email)) _ = Run(async () => await Account.ForgotPasswordAsync(_email.value.Trim()), done: "If that address has an account, a new code is on its way.");
                    break;
            }
        }

        /// <summary>True if every field has something in it; otherwise says which one is empty.</summary>
        bool Required(params TextField[] fields)
        {
            foreach (TextField field in fields)
            {
                if (!string.IsNullOrWhiteSpace(field.value)) continue;
                _formStatus.style.color = Bad;
                _formStatus.text = "Fill in " + field.label.ToLowerInvariant() + ".";
                field.Focus();
                return false;
            }
            return true;
        }

        /// <summary>Runs a request with the buttons off, then shows how it went.</summary>
        /// <param name="andThen">The form to move to if it worked; by default the form closes.</param>
        /// <param name="done">What to say if it worked and the form stays open or moves on.</param>
        async Task Run(Func<Task<AccountResult>> work, Form andThen = Form.None, string done = null)
        {
            _busy = true;
            SetButtonsEnabled(false);
            _formStatus.style.color = Text;
            _formStatus.text = "One moment...";

            AccountResult result;
            try
            {
                result = await work();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                result = new AccountResult { Message = "Something went wrong. Try again." };
            }
            finally
            {
                _busy = false;
                SetButtonsEnabled(true);
            }

            if (result != null && !result.Ok)
            {
                _formStatus.style.color = Bad;
                _formStatus.text = result.FirstMessage;
                return;
            }

            if (andThen != Form.None || done != null)
            {
                string email = _email.value;
                Show(andThen != Form.None ? andThen : _form);
                if (andThen == Form.Reset || andThen == Form.SignIn) _email.value = email;
                if (andThen == Form.SignIn) _login.value = email;
                _formStatus.style.color = Good;
                _formStatus.text = done ?? "";
                if (_form == Form.Verify && Account.Account != null && Account.Account.emailVerified) Show(Form.None);
            }
            else
            {
                Show(Form.None);
            }
            Refresh();
        }

        void SetButtonsEnabled(bool enabled)
        {
            foreach (Button button in new[] { _submit, _secondary, _back, _forgotLink, _signInButton, _registerButton, _verifyButton, _signOutButton })
                button.SetEnabled(enabled);
        }

        // ------------------------------------------------------------------ helpers

        static TextField Field(string label, int maxLength)
        {
            var field = new TextField(label) { maxLength = maxLength };
            field.AddToClassList("loadout-name");
            field.style.fontSize = 16;
            field.style.marginTop = 4;
            field.style.marginBottom = 4;
            return field;
        }

        static Label Label(string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        static Button SmallButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("pause-button");
            button.style.minWidth = 0;
            button.style.height = 30;
            button.style.fontSize = 14;
            button.style.marginLeft = 0;
            button.style.marginRight = 8;
            button.style.paddingLeft = button.style.paddingRight = 12;
            return button;
        }

        static Button MenuButton(string text, bool primary, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("pause-button");
            if (primary) button.AddToClassList("primary");
            return button;
        }

        static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
