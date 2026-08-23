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
        timerId: null
    };

    const alertBox = document.querySelector("#alert");
    const views = [...document.querySelectorAll("[data-view]")];
    const loginForm = document.querySelector("#login-form");
    const registerForm = document.querySelector("#register-form");
    const otpForm = document.querySelector("#otp-form");
    const otpInput = document.querySelector("#otp-input");
    const resendButton = document.querySelector("#resend-button");
    const otpCountdown = document.querySelector("#otp-countdown");
    const resendCountdown = document.querySelector("#resend-countdown");
    const profileName = document.querySelector("#profile-name");
    const profileEmail = document.querySelector("#profile-email");

    const errorMessages = Object.freeze({
        VALIDATION_ERROR: "Dữ liệu chưa hợp lệ. Vui lòng kiểm tra lại các trường.",
        EMAIL_ALREADY_REGISTERED: "Email này đã được đăng ký.",
        INVALID_CREDENTIALS: "Email hoặc mật khẩu không hợp lệ.",
        OTP_VERIFICATION_FAILED: "Mã OTP không hợp lệ, đã hết hạn hoặc không còn sử dụng được.",
        RESEND_NOT_AVAILABLE: "Không thể gửi lại OTP cho yêu cầu này. Vui lòng đăng nhập lại.",
        RESEND_COOLDOWN: "Bạn cần chờ thêm trước khi gửi lại OTP.",
        OTP_DELIVERY_UNAVAILABLE: "Chưa thể gửi email OTP. Vui lòng thử lại sau.",
        RATE_LIMITED: "Bạn thao tác quá nhiều lần. Vui lòng thử lại sau.",
        ACCOUNT_INACTIVE: "Tài khoản không còn hoạt động.",
        INTERNAL_ERROR: "Hệ thống đang bận. Vui lòng thử lại sau."
    });

    function showView(name) {
        views.forEach(view => {
            view.hidden = view.dataset.view !== name;
        });
        updateFlow(name);
        clearAlert();
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
        alertBox.textContent = message;
        alertBox.className = `alert ${type}`;
        alertBox.hidden = false;
    }

    function clearAlert() {
        alertBox.textContent = "";
        alertBox.className = "alert";
        alertBox.hidden = true;
    }

    function setButtonBusy(button, busy) {
        button.disabled = busy;
        button.dataset.busy = busy ? "true" : "false";
        button.textContent = busy ? button.dataset.loadingText : button.dataset.idleText;
    }

    function clearChallenge() {
        state.challengeId = null;
        state.expiresAt = null;
        state.resendAvailableAt = null;
        otpInput.value = "";
        stopTimer();
    }

    function clearSession() {
        sessionStorage.removeItem(tokenStorageKey);
        clearChallenge();
        profileName.textContent = "—";
        profileEmail.textContent = "—";
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
        const now = Date.now();
        const otpSeconds = secondsUntil(state.expiresAt, now);
        const resendSeconds = secondsUntil(state.resendAvailableAt, now);

        otpCountdown.textContent = otpSeconds > 0 ? formatDuration(otpSeconds) : "Đã hết hạn";
        otpCountdown.classList.toggle("expired", otpSeconds === 0);
        resendCountdown.textContent = resendSeconds > 0 ? `${resendSeconds} giây` : "Có thể gửi";
        resendButton.disabled = resendSeconds > 0 || resendButton.dataset.busy === "true";
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

    function friendlyError(error) {
        if (error?.code && errorMessages[error.code]) {
            return errorMessages[error.code];
        }
        if (error?.status === 429) {
            return errorMessages.RATE_LIMITED;
        }
        if (error?.status === 401) {
            return "Thông tin xác thực không hợp lệ.";
        }
        if (error?.status === 409) {
            return errorMessages.EMAIL_ALREADY_REGISTERED;
        }
        if (error?.status === 0) {
            return "Không kết nối được tới máy chủ. Vui lòng kiểm tra ứng dụng đang chạy.";
        }
        return errorMessages.INTERNAL_ERROR;
    }

    registerForm.addEventListener("submit", async event => {
        event.preventDefault();
        clearAlert();
        if (!registerForm.reportValidity()) {
            return;
        }

        const fullName = document.querySelector("#register-name").value.trim();
        const email = document.querySelector("#register-email").value.trim();
        const passwordInput = document.querySelector("#register-password");
        const confirmInput = document.querySelector("#register-confirm-password");
        if (passwordInput.value !== confirmInput.value) {
            showAlert("Mật khẩu xác nhận không khớp.");
            return;
        }

        const body = { fullName, email, password: passwordInput.value };
        passwordInput.value = "";
        confirmInput.value = "";
        const submitButton = registerForm.querySelector("[type='submit']");
        setButtonBusy(submitButton, true);

        try {
            await request(api.register, {
                method: "POST",
                body: JSON.stringify(body)
            });
            registerForm.reset();
            document.querySelector("#login-email").value = email;
            showView("login");
            showAlert("Đăng ký thành công. Bạn có thể đăng nhập.", "success");
        } catch (error) {
            showAlert(friendlyError(error));
        } finally {
            setButtonBusy(submitButton, false);
        }
    });

    loginForm.addEventListener("submit", async event => {
        event.preventDefault();
        clearAlert();
        if (!loginForm.reportValidity()) {
            return;
        }

        const email = document.querySelector("#login-email").value.trim();
        const passwordInput = document.querySelector("#login-password");
        const body = { email, password: passwordInput.value };
        passwordInput.value = "";
        const submitButton = loginForm.querySelector("[type='submit']");
        setButtonBusy(submitButton, true);
        clearSession();

        try {
            const result = await request(api.login, {
                method: "POST",
                body: JSON.stringify(body)
            });
            if (!result?.requiresOtp || !result.challengeId) {
                throw { status: 500, code: "INTERNAL_ERROR" };
            }

            state.challengeId = result.challengeId;
            state.expiresAt = result.expiresAt;
            state.resendAvailableAt = result.resendAvailableAt;
            showView("otp");
            startTimer();
            otpInput.focus();
            showAlert("Mật khẩu hợp lệ. Hãy nhập OTP đã gửi qua email.", "success");
        } catch (error) {
            showAlert(friendlyError(error));
        } finally {
            setButtonBusy(submitButton, false);
        }
    });

    otpInput.addEventListener("input", () => {
        otpInput.value = otpInput.value.replace(/[^0-9]/g, "").slice(0, 6);
    });

    otpForm.addEventListener("submit", async event => {
        event.preventDefault();
        clearAlert();
        if (!state.challengeId) {
            showAlert("Phiên OTP không còn tồn tại. Vui lòng đăng nhập lại.");
            showView("login");
            return;
        }
        if (!otpForm.reportValidity()) {
            return;
        }

        const otp = otpInput.value;
        otpInput.value = "";
        const submitButton = otpForm.querySelector("[type='submit']");
        setButtonBusy(submitButton, true);

        try {
            const result = await request(api.verifyOtp, {
                method: "POST",
                body: JSON.stringify({ challengeId: state.challengeId, otp })
            });
            if (!result?.accessToken) {
                throw { status: 500, code: "INTERNAL_ERROR" };
            }

            sessionStorage.setItem(tokenStorageKey, result.accessToken);
            clearChallenge();
            showView("dashboard");
            if (await loadProfile(false)) {
                showAlert("OTP hợp lệ. Phiên đăng nhập đã được tạo.", "success");
            }
        } catch (error) {
            showAlert(friendlyError(error));
            otpInput.focus();
        } finally {
            setButtonBusy(submitButton, false);
        }
    });

    resendButton.addEventListener("click", async () => {
        clearAlert();
        if (!state.challengeId) {
            showAlert("Phiên OTP không còn tồn tại. Vui lòng đăng nhập lại.");
            showView("login");
            return;
        }

        setButtonBusy(resendButton, true);
        try {
            const result = await request(api.resendOtp, {
                method: "POST",
                body: JSON.stringify({ challengeId: state.challengeId })
            });
            state.challengeId = result.challengeId;
            state.expiresAt = result.expiresAt;
            state.resendAvailableAt = result.resendAvailableAt;
            otpInput.value = "";
            startTimer();
            otpInput.focus();
            showAlert("Đã gửi OTP mới. Mã trước đó không còn hiệu lực.", "success");
        } catch (error) {
            const retryAfter = Number.parseInt(error?.retryAfter, 10);
            if (error?.status === 429 && Number.isFinite(retryAfter) && retryAfter > 0) {
                state.resendAvailableAt = new Date(Date.now() + Math.min(retryAfter, 3600) * 1000).toISOString();
                startTimer();
            }
            showAlert(friendlyError(error));
        } finally {
            setButtonBusy(resendButton, false);
            updateTimers();
        }
    });

    async function loadProfile(redirectOnFailure = true) {
        const token = sessionStorage.getItem(tokenStorageKey);
        if (!token) {
            if (redirectOnFailure) {
                showView("login");
                showAlert("Bạn cần đăng nhập để truy cập trang bảo vệ.", "info");
            }
            return false;
        }

        try {
            const profile = await request(api.currentUser, {
                method: "GET",
                headers: { Authorization: `Bearer ${token}` }
            });
            profileName.textContent = profile.fullName || "—";
            profileEmail.textContent = profile.email || "—";
            showView("dashboard");
            return true;
        } catch (error) {
            clearSession();
            showView("login");
            showAlert(
                error?.status === 401 || error?.status === 403
                    ? "Phiên đăng nhập đã hết hạn hoặc không còn hợp lệ."
                    : friendlyError(error),
                "info");
            return false;
        }
    }

    document.querySelector("#check-profile-button").addEventListener("click", async event => {
        const button = event.currentTarget;
        clearAlert();
        setButtonBusy(button, true);
        await loadProfile(true);
        if (!document.querySelector("[data-view='dashboard']").hidden) {
            showAlert("API bảo vệ trả về 200 OK.", "success");
        }
        setButtonBusy(button, false);
    });

    document.querySelector("#logout-button").addEventListener("click", () => {
        clearSession();
        loginForm.reset();
        registerForm.reset();
        showView("login");
        showAlert("Đã đăng xuất và xóa JWT khỏi phiên trình duyệt.", "success");
    });

    document.querySelectorAll("[data-go]").forEach(button => {
        button.addEventListener("click", () => {
            clearSession();
            loginForm.reset();
            registerForm.reset();
            otpForm.reset();
            showView(button.dataset.go);
        });
    });

    document.querySelector("[data-cancel-otp]").addEventListener("click", () => {
        clearChallenge();
        showView("login");
        showAlert("Đã hủy bước OTP. Vui lòng đăng nhập lại.", "info");
    });

    if (sessionStorage.getItem(tokenStorageKey)) {
        loadProfile(true);
    } else {
        showView("login");
    }
})();
