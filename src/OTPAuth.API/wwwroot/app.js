(() => {
    "use strict";

    const api = Object.freeze({
        register: "/api/auth/register",
        login: "/api/auth/login",
        verifyOtp: "/api/auth/verify-otp",
        resendOtp: "/api/auth/resend-otp",
        currentUser: "/api/auth/me"
    });
    const tokenStorageKey = "otpAuth.accessToken";
    const state = {
        challengeId: null,
        expiresAt: null,
        resendAvailableAt: null,
        timerId: null,
        authActionInProgress: false,
        otpActionInProgress: false,
        sessionGeneration: 0
    };

    const alertBox = document.querySelector("#alert");
    const views = [...document.querySelectorAll("[data-view]")];
    const loginForm = document.querySelector("#login-form");
    const loginEmailInput = document.querySelector("#login-email");
    const loginPasswordInput = document.querySelector("#login-password");
    const registerForm = document.querySelector("#register-form");
    const registerNameInput = document.querySelector("#register-name");
    const registerEmailInput = document.querySelector("#register-email");
    const registerPasswordInput = document.querySelector("#register-password");
    const registerConfirmInput = document.querySelector("#register-confirm-password");
    const otpForm = document.querySelector("#otp-form");
    const otpInput = document.querySelector("#otp-input");
    const otpSubmitButton = otpForm.querySelector("[type='submit']");
    const otpDestination = document.querySelector("#otp-destination");
    const otpTimerMessage = document.querySelector("#otp-timer-message");
    const otpTimerLabel = document.querySelector("#otp-timer-label");
    const otpCountdown = document.querySelector("#otp-countdown");
    const resendTimerLabel = document.querySelector("#resend-timer-label");
    const resendCountdown = document.querySelector("#resend-countdown");
    const resendPeriod = document.querySelector("#resend-period");
    const resendButton = document.querySelector("#resend-button");
    const cancelOtpButton = document.querySelector("[data-cancel-otp]");
    const profileName = document.querySelector("#profile-name");
    const profileEmail = document.querySelector("#profile-email");
    const checkProfileButton = document.querySelector("#check-profile-button");
    const logoutButton = document.querySelector("#logout-button");
    const navigationButtons = [...document.querySelectorAll("[data-go]")];

    function showView(name, moveFocus = true) {
        let activeView = null;
        views.forEach(view => {
            view.hidden = view.dataset.view !== name;
            if (!view.hidden) {
                activeView = view;
            }
        });
        updateFlow(name);
        clearAlert();

        if (moveFocus && activeView) {
            window.requestAnimationFrame(() => {
                activeView.querySelector("h2")?.focus();
            });
        }
    }

    function updateFlow(viewName) {
        const stage = viewName === "otp" ? 2 : viewName === "dashboard" ? 3 : 1;
        document.querySelectorAll("[data-flow-step]").forEach((item, index) => {
            const step = index + 1;
            item.classList.toggle("active", step === stage);
            item.classList.toggle("complete", step < stage);
        });
    }

    function showAlert(message, type = "error") {
        const labels = {
            error: "Có lỗi:",
            success: "Thành công:",
            warning: "Lưu ý:",
            info: "Thông tin:"
        };
        alertBox.textContent = `${labels[type] || labels.info} ${message}`;
        alertBox.className = `alert ${type}`;
        alertBox.setAttribute("role", type === "error" ? "alert" : "status");
        alertBox.setAttribute("aria-live", type === "error" ? "assertive" : "polite");
        alertBox.hidden = false;
    }

    function clearAlert() {
        alertBox.textContent = "";
        alertBox.className = "alert";
        alertBox.setAttribute("role", "status");
        alertBox.setAttribute("aria-live", "polite");
        alertBox.hidden = true;
    }

    function setButtonBusy(button, busy) {
        button.disabled = busy;
        button.dataset.busy = busy ? "true" : "false";
        button.setAttribute("aria-busy", busy ? "true" : "false");
        button.textContent = busy
            ? button.dataset.loadingText || button.dataset.idleText
            : button.dataset.idleText;
        button.closest("form")?.setAttribute("aria-busy", busy ? "true" : "false");
    }

    function setOtpActionBusy(button, busy) {
        state.otpActionInProgress = busy;
        setButtonBusy(button, busy);
        updateTimers();
    }

    function setAuthActionBusy(button, busy) {
        state.authActionInProgress = busy;
        setButtonBusy(button, busy);
        navigationButtons.forEach(navigationButton => {
            navigationButton.disabled = busy;
        });
    }

    function clearChallenge() {
        state.challengeId = null;
        state.expiresAt = null;
        state.resendAvailableAt = null;
        state.otpActionInProgress = false;
        otpDestination.textContent = "email của bạn";
        otpInput.value = "";
        clearFieldError(otpInput);
        stopTimer();
    }

    function clearSession() {
        sessionStorage.removeItem(tokenStorageKey);
        state.sessionGeneration += 1;
        clearChallenge();
        profileName.textContent = "—";
        profileEmail.textContent = "—";
    }

    function resetForms() {
        loginForm.reset();
        registerForm.reset();
        otpForm.reset();
        document.querySelectorAll("input").forEach(clearFieldError);
        resetPasswordVisibility();
    }

    function stopTimer() {
        if (state.timerId !== null) {
            window.clearInterval(state.timerId);
            state.timerId = null;
        }
    }

    function startTimer() {
        stopTimer();
        updateTimers();
        state.timerId = window.setInterval(updateTimers, 1000);
    }

    function updateTimers() {
        const hasChallenge = Boolean(state.challengeId);
        const now = Date.now();
        const otpSeconds = secondsUntil(state.expiresAt, now);
        const resendSeconds = secondsUntil(state.resendAvailableAt, now);
        const otpExpired = hasChallenge && otpSeconds === 0;

        if (otpExpired) {
            otpTimerLabel.textContent = "Mã xác thực đã hết hạn.";
            otpCountdown.textContent = "";
            otpCountdown.hidden = true;
            otpTimerMessage.classList.add("expired");
        } else {
            otpTimerLabel.textContent = "Mã sẽ hết hạn sau";
            otpCountdown.textContent = formatDuration(otpSeconds);
            otpCountdown.hidden = false;
            otpTimerMessage.classList.remove("expired");
        }

        if (hasChallenge && resendSeconds > 0) {
            resendTimerLabel.textContent = "Bạn có thể gửi lại mã sau";
            resendCountdown.textContent = `${resendSeconds} giây`;
            resendCountdown.hidden = false;
            resendPeriod.hidden = false;
        } else {
            resendTimerLabel.textContent = "Bạn có thể gửi lại mã ngay.";
            resendCountdown.textContent = "";
            resendCountdown.hidden = true;
            resendPeriod.hidden = true;
        }

        otpInput.disabled = !hasChallenge || otpExpired || state.otpActionInProgress;
        otpSubmitButton.disabled = !hasChallenge || otpExpired || state.otpActionInProgress;
        resendButton.disabled = !hasChallenge || resendSeconds > 0 || state.otpActionInProgress;
        cancelOtpButton.disabled = state.otpActionInProgress;

        if (hasChallenge && otpSeconds === 0 && resendSeconds === 0) {
            stopTimer();
        }
    }

    function secondsUntil(timestamp, now) {
        if (!timestamp) {
            return 0;
        }
        const parsed = Date.parse(timestamp);
        return Number.isFinite(parsed) ? Math.max(0, Math.ceil((parsed - now) / 1000)) : 0;
    }

    function formatDuration(totalSeconds) {
        const minutes = Math.floor(totalSeconds / 60).toString().padStart(2, "0");
        const seconds = (totalSeconds % 60).toString().padStart(2, "0");
        return `${minutes}:${seconds}`;
    }

    function maskEmail(email) {
        const separatorIndex = email.indexOf("@");
        if (separatorIndex <= 0 || separatorIndex === email.length - 1) {
            return "email của bạn";
        }

        const localPart = email.slice(0, separatorIndex);
        const domain = email.slice(separatorIndex + 1);
        const visibleLength = Math.min(2, localPart.length);
        return `${localPart.slice(0, visibleLength)}***@${domain}`;
    }

    function setFieldError(input, message) {
        const errorId = input.getAttribute("aria-describedby");
        const errorElement = errorId ? document.getElementById(errorId) : null;
        input.setAttribute("aria-invalid", "true");
        if (errorElement) {
            errorElement.textContent = message;
        }
    }

    function clearFieldError(input) {
        const errorId = input.getAttribute("aria-describedby");
        const errorElement = errorId ? document.getElementById(errorId) : null;
        input.removeAttribute("aria-invalid");
        if (errorElement) {
            errorElement.textContent = "";
        }
    }

    function focusFirstInvalid(inputs) {
        inputs.find(input => input.getAttribute("aria-invalid") === "true")?.focus();
    }

    function validateLoginForm() {
        const inputs = [loginEmailInput, loginPasswordInput];
        inputs.forEach(clearFieldError);
        loginEmailInput.value = loginEmailInput.value.trim();

        if (!loginEmailInput.value) {
            setFieldError(loginEmailInput, "Vui lòng nhập email.");
        } else if (loginEmailInput.validity.typeMismatch) {
            setFieldError(loginEmailInput, "Email không đúng định dạng.");
        }

        if (!loginPasswordInput.value) {
            setFieldError(loginPasswordInput, "Vui lòng nhập mật khẩu.");
        } else if (loginPasswordInput.value.length < 8) {
            setFieldError(loginPasswordInput, "Mật khẩu phải có ít nhất 8 ký tự.");
        }

        focusFirstInvalid(inputs);
        return inputs.every(input => input.getAttribute("aria-invalid") !== "true");
    }

    function validateRegisterForm() {
        const inputs = [registerNameInput, registerEmailInput, registerPasswordInput, registerConfirmInput];
        inputs.forEach(clearFieldError);
        registerNameInput.value = registerNameInput.value.trim();
        registerEmailInput.value = registerEmailInput.value.trim();

        if (!registerNameInput.value) {
            setFieldError(registerNameInput, "Vui lòng nhập họ và tên.");
        } else if (registerNameInput.value.length < 2) {
            setFieldError(registerNameInput, "Họ và tên phải có ít nhất 2 ký tự.");
        }

        if (!registerEmailInput.value) {
            setFieldError(registerEmailInput, "Vui lòng nhập email.");
        } else if (registerEmailInput.validity.typeMismatch) {
            setFieldError(registerEmailInput, "Email không đúng định dạng.");
        }

        if (!registerPasswordInput.value) {
            setFieldError(registerPasswordInput, "Vui lòng nhập mật khẩu.");
        } else if (registerPasswordInput.value.length < 8) {
            setFieldError(registerPasswordInput, "Mật khẩu phải có từ 8 đến 128 ký tự.");
        }

        if (!registerConfirmInput.value) {
            setFieldError(registerConfirmInput, "Vui lòng nhập lại mật khẩu.");
        } else if (registerPasswordInput.value !== registerConfirmInput.value) {
            setFieldError(registerConfirmInput, "Mật khẩu nhập lại không khớp.");
        }

        focusFirstInvalid(inputs);
        return inputs.every(input => input.getAttribute("aria-invalid") !== "true");
    }

    function validateOtpForm() {
        clearFieldError(otpInput);
        if (!/^[0-9]{6}$/.test(otpInput.value)) {
            setFieldError(otpInput, "Vui lòng nhập mã xác thực gồm 6 chữ số.");
            otpInput.focus();
            return false;
        }
        return true;
    }

    function resetPasswordVisibility() {
        document.querySelectorAll("[data-password-toggle]").forEach(button => {
            const input = document.getElementById(button.dataset.passwordToggle);
            input.type = "password";
            button.textContent = "Hiện mật khẩu";
            button.setAttribute("aria-pressed", "false");
        });
    }

    async function request(url, options = {}) {
        let response;
        try {
            response = await fetch(url, {
                ...options,
                cache: "no-store",
                headers: {
                    "Content-Type": "application/json",
                    ...(options.headers || {})
                }
            });
        } catch {
            throw { status: 0, code: "NETWORK_ERROR" };
        }

        const contentType = response.headers.get("content-type") || "";
        const body = contentType.includes("json")
            ? await response.json().catch(() => null)
            : null;

        if (!response.ok) {
            throw {
                status: response.status,
                code: body?.code,
                retryAfter: response.headers.get("Retry-After") || body?.retryAfterSeconds
            };
        }

        return body;
    }

    function friendlyError(error, context) {
        if (error?.status === 0) {
            return "Không thể kết nối đến hệ thống. Vui lòng thử lại sau.";
        }

        if (context === "register") {
            if (error?.code === "EMAIL_ALREADY_REGISTERED" || error?.status === 409) {
                return "Email này đã được sử dụng.";
            }
            if (error?.code === "VALIDATION_ERROR") {
                return "Thông tin đăng ký chưa hợp lệ. Vui lòng kiểm tra lại.";
            }
            if (error?.status === 429 || error?.code === "RATE_LIMITED") {
                return "Bạn đã đăng ký quá nhiều lần. Vui lòng thử lại sau.";
            }
            return "Không thể tạo tài khoản lúc này. Vui lòng thử lại sau.";
        }

        if (context === "login") {
            if (error?.code === "INVALID_CREDENTIALS" || error?.status === 401) {
                return "Email hoặc mật khẩu không chính xác.";
            }
            if (error?.code === "ACCOUNT_INACTIVE") {
                return "Tài khoản hiện không thể đăng nhập.";
            }
            if (error?.status === 429 || error?.code === "RATE_LIMITED") {
                return "Bạn đã thử đăng nhập quá nhiều lần. Vui lòng thử lại sau.";
            }
            if (error?.code === "OTP_DELIVERY_UNAVAILABLE") {
                return "Chưa thể gửi mã xác thực. Vui lòng thử đăng nhập lại sau.";
            }
            return "Không thể đăng nhập lúc này. Vui lòng thử lại sau.";
        }

        if (context === "verify") {
            if (error?.status === 429 || error?.code === "RATE_LIMITED") {
                return "Bạn đã nhập mã quá nhiều lần. Vui lòng thử lại sau.";
            }
            if (error?.code === "OTP_VERIFICATION_FAILED" || error?.status === 401) {
                return "Không thể xác thực mã OTP. Vui lòng kiểm tra mã mới nhất và thử lại.";
            }
            return "Không thể xác thực mã OTP. Vui lòng thử lại.";
        }

        if (context === "resend") {
            if (error?.code === "RESEND_COOLDOWN") {
                return "Vui lòng chờ trước khi yêu cầu mã mới.";
            }
            if (error?.status === 429 || error?.code === "RATE_LIMITED") {
                return "Bạn đã yêu cầu mã quá nhiều lần. Vui lòng thử lại sau.";
            }
            if (error?.code === "OTP_DELIVERY_UNAVAILABLE") {
                return "Chưa thể gửi mã xác thực mới. Vui lòng đăng nhập lại sau.";
            }
            if (error?.code === "RESEND_NOT_AVAILABLE") {
                return "Không thể gửi lại mã. Vui lòng đăng nhập lại để nhận mã mới.";
            }
            return "Không thể gửi mã mới lúc này. Vui lòng thử lại sau.";
        }

        if (error?.status === 401 || error?.status === 403) {
            return "Phiên đăng nhập đã hết hạn hoặc không còn hiệu lực.";
        }
        return "Hệ thống đang bận. Vui lòng thử lại sau.";
    }

    registerForm.addEventListener("submit", async event => {
        event.preventDefault();
        if (state.authActionInProgress) {
            return;
        }
        clearAlert();
        if (!validateRegisterForm()) {
            return;
        }

        const email = registerEmailInput.value;
        const body = {
            fullName: registerNameInput.value,
            email,
            password: registerPasswordInput.value
        };
        registerPasswordInput.value = "";
        registerConfirmInput.value = "";
        resetPasswordVisibility();
        const submitButton = registerForm.querySelector("[type='submit']");
        setAuthActionBusy(submitButton, true);

        try {
            await request(api.register, {
                method: "POST",
                body: JSON.stringify(body)
            });
            registerForm.reset();
            loginEmailInput.value = email;
            showView("login");
            showAlert("Đăng ký thành công. Bạn có thể đăng nhập ngay.", "success");
        } catch (error) {
            showAlert(friendlyError(error, "register"));
        } finally {
            setAuthActionBusy(submitButton, false);
        }
    });

    loginForm.addEventListener("submit", async event => {
        event.preventDefault();
        if (state.authActionInProgress) {
            return;
        }
        clearAlert();
        if (!validateLoginForm()) {
            return;
        }

        const email = loginEmailInput.value;
        const body = { email, password: loginPasswordInput.value };
        loginPasswordInput.value = "";
        resetPasswordVisibility();
        const submitButton = loginForm.querySelector("[type='submit']");
        setAuthActionBusy(submitButton, true);
        clearSession();

        try {
            const result = await request(api.login, {
                method: "POST",
                body: JSON.stringify(body)
            });
            if (!result?.requiresOtp || !result.challengeId || !result.expiresAt || !result.resendAvailableAt) {
                throw { status: 500, code: "INTERNAL_ERROR" };
            }

            state.challengeId = result.challengeId;
            state.expiresAt = result.expiresAt;
            state.resendAvailableAt = result.resendAvailableAt;
            otpDestination.textContent = maskEmail(email);
            loginEmailInput.value = "";
            showView("otp", false);
            startTimer();
            otpInput.focus();
            showAlert("Mã xác thực đã được gửi đến email của bạn.", "success");
        } catch (error) {
            showAlert(friendlyError(error, "login"));
        } finally {
            setAuthActionBusy(submitButton, false);
        }
    });

    otpInput.addEventListener("input", () => {
        otpInput.value = otpInput.value.replace(/[^0-9]/g, "").slice(0, 6);
        clearFieldError(otpInput);
    });

    otpForm.addEventListener("submit", async event => {
        event.preventDefault();
        if (state.otpActionInProgress) {
            return;
        }
        clearAlert();
        if (!state.challengeId) {
            showView("login");
            showAlert("Phiên xác thực không còn hiệu lực. Vui lòng đăng nhập lại.");
            return;
        }
        if (!validateOtpForm()) {
            return;
        }

        const otp = otpInput.value;
        otpInput.value = "";
        setOtpActionBusy(otpSubmitButton, true);
        let shouldRefocusOtp = false;

        try {
            const result = await request(api.verifyOtp, {
                method: "POST",
                body: JSON.stringify({ challengeId: state.challengeId, otp })
            });
            if (!result?.accessToken) {
                throw { status: 500, code: "VERIFY_RESPONSE_INVALID" };
            }

            sessionStorage.setItem(tokenStorageKey, result.accessToken);
            state.sessionGeneration += 1;
            clearChallenge();
            showView("dashboard");
            if (await loadProfile(false)) {
                showAlert("Bạn đã đăng nhập và xác thực thành công.", "success");
            }
        } catch (error) {
            if (error?.code === "VERIFY_RESPONSE_INVALID") {
                clearChallenge();
                showView("login");
                showAlert("Không thể hoàn tất đăng nhập. Vui lòng thử đăng nhập lại.");
                return;
            }
            showAlert(friendlyError(error, "verify"));
            shouldRefocusOtp = true;
        } finally {
            setOtpActionBusy(otpSubmitButton, false);
            if (shouldRefocusOtp && !document.querySelector("[data-view='otp']").hidden && !otpInput.disabled) {
                otpInput.focus();
            }
        }
    });

    resendButton.addEventListener("click", async () => {
        if (state.otpActionInProgress) {
            return;
        }
        clearAlert();
        if (!state.challengeId) {
            showView("login");
            showAlert("Phiên xác thực không còn hiệu lực. Vui lòng đăng nhập lại.");
            return;
        }

        setOtpActionBusy(resendButton, true);
        let shouldRefocusOtp = false;
        try {
            const result = await request(api.resendOtp, {
                method: "POST",
                body: JSON.stringify({ challengeId: state.challengeId })
            });
            if (!result?.challengeId || !result.expiresAt || !result.resendAvailableAt) {
                throw { status: 500, code: "RESEND_RESPONSE_INVALID" };
            }

            state.challengeId = result.challengeId;
            state.expiresAt = result.expiresAt;
            state.resendAvailableAt = result.resendAvailableAt;
            otpInput.value = "";
            clearFieldError(otpInput);
            startTimer();
            shouldRefocusOtp = true;
            showAlert("Mã xác thực mới đã được gửi đến email của bạn.", "success");
        } catch (error) {
            const retryAfter = Number.parseInt(error?.retryAfter, 10);
            if (error?.status === 429 && Number.isFinite(retryAfter) && retryAfter > 0) {
                state.resendAvailableAt = new Date(Date.now() + Math.min(retryAfter, 3600) * 1000).toISOString();
                startTimer();
            }

            if (error?.code === "OTP_DELIVERY_UNAVAILABLE" ||
                error?.code === "RESEND_NOT_AVAILABLE" ||
                error?.code === "RESEND_RESPONSE_INVALID") {
                const message = friendlyError(error, "resend");
                clearChallenge();
                showView("login");
                showAlert(message);
                return;
            }

            showAlert(friendlyError(error, "resend"));
            shouldRefocusOtp = true;
        } finally {
            setOtpActionBusy(resendButton, false);
            if (shouldRefocusOtp && !document.querySelector("[data-view='otp']").hidden && !otpInput.disabled) {
                otpInput.focus();
            }
        }
    });

    async function loadProfile(redirectOnFailure = true) {
        const token = sessionStorage.getItem(tokenStorageKey);
        if (!token) {
            if (redirectOnFailure) {
                showView("login");
                showAlert("Bạn cần đăng nhập để tiếp tục.", "info");
            }
            return false;
        }

        const requestGeneration = state.sessionGeneration;
        try {
            const profile = await request(api.currentUser, {
                method: "GET",
                headers: { Authorization: `Bearer ${token}` }
            });
            if (requestGeneration !== state.sessionGeneration || token !== sessionStorage.getItem(tokenStorageKey)) {
                return false;
            }

            profileName.textContent = profile.fullName || "—";
            profileEmail.textContent = profile.email || "—";
            showView("dashboard", false);
            return true;
        } catch (error) {
            if (requestGeneration !== state.sessionGeneration) {
                return false;
            }

            if (error?.status === 401 || error?.status === 403) {
                clearSession();
                showView("login");
                showAlert("Phiên đăng nhập đã hết hạn hoặc không còn hiệu lực.", "info");
            } else if (redirectOnFailure) {
                showView("login");
                showAlert("Chưa thể kiểm tra phiên đăng nhập. Vui lòng thử lại sau.", "warning");
            } else {
                showAlert("Đăng nhập thành công nhưng chưa thể tải thông tin tài khoản.", "warning");
            }
            return false;
        }
    }

    checkProfileButton.addEventListener("click", async () => {
        clearAlert();
        setButtonBusy(checkProfileButton, true);
        logoutButton.disabled = true;
        try {
            if (await loadProfile(false)) {
                showAlert("Phiên đăng nhập đang hoạt động.", "success");
            }
        } finally {
            setButtonBusy(checkProfileButton, false);
            logoutButton.disabled = false;
        }
    });

    logoutButton.addEventListener("click", () => {
        clearSession();
        resetForms();
        showView("login");
        showAlert("Bạn đã đăng xuất.", "success");
    });

    navigationButtons.forEach(button => {
        button.addEventListener("click", () => {
            clearSession();
            resetForms();
            showView(button.dataset.go);
        });
    });

    cancelOtpButton.addEventListener("click", () => {
        clearChallenge();
        showView("login");
        showAlert("Bạn đã quay lại bước đăng nhập.", "info");
    });

    document.querySelectorAll("[data-password-toggle]").forEach(button => {
        button.addEventListener("click", () => {
            const input = document.getElementById(button.dataset.passwordToggle);
            const shouldShow = input.type === "password";
            input.type = shouldShow ? "text" : "password";
            button.textContent = shouldShow ? "Ẩn mật khẩu" : "Hiện mật khẩu";
            button.setAttribute("aria-pressed", shouldShow ? "true" : "false");
            input.focus();
        });
    });

    [loginEmailInput, loginPasswordInput, registerNameInput, registerEmailInput,
        registerPasswordInput, registerConfirmInput].forEach(input => {
        input.addEventListener("input", () => clearFieldError(input));
    });

    if (sessionStorage.getItem(tokenStorageKey)) {
        loadProfile(true);
    } else {
        showView("login");
    }
})();
