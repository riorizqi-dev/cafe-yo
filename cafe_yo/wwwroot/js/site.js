document.addEventListener("DOMContentLoaded", () => {
  const storageKey = "nr_tableNumber";
  const localTableKey = "nr_tableNumber_local";
  const sessionTableKey = "nr_tableNumber_session";
  const pendingMemberTableKey = "nr_pending_member_table";
  const authNoticeKey = "nr_auth_notice";
  const badge = document.getElementById("tableBadgeValue");
  const input = document.getElementById("tableNumberInput");
  const saveBtn = document.getElementById("tableSaveBtn");
  const resetBtn = document.getElementById("tableResetBtn");
  const tableModalEl = document.getElementById("tableModal");
  const tableGridEl = document.getElementById("tablePickerGrid");
  const tableHintEl = document.getElementById("tablePickerHint");
  const authModalEl = document.getElementById("authModal");
  const membershipGuest = document.getElementById("membershipGuest");
  const membershipMember = document.getElementById("membershipMember");

  const getStoredTable = () => {
    const parts = document.cookie.split(";").map((part) => part.trim());
    const cookie = parts.find((part) => part.startsWith(`${storageKey}=`));
    return cookie ? decodeURIComponent(cookie.split("=").slice(1).join("=")) : "";
  };

  const setStoredTable = (value) => {
    const expires = new Date();
    expires.setDate(expires.getDate() + 30);
    document.cookie = `${storageKey}=${encodeURIComponent(value)}; expires=${expires.toUTCString()}; path=/`;
  };

  const clearStoredTable = () => {
    document.cookie = `${storageKey}=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/`;
    document.cookie = `nr_tableLocked=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/`;
    try {
      localStorage.removeItem(localTableKey);
      sessionStorage.removeItem(sessionTableKey);
    } catch (_e) {}
  };

  const isTableLocked = () => {
    const parts = document.cookie.split(";").map((part) => part.trim());
    const cookie = parts.find((part) => part.startsWith("nr_tableLocked="));
    const value = cookie ? decodeURIComponent(cookie.split("=").slice(1).join("=")) : "";
    return value === "1";
  };

  const setBadge = (value) => {
    if (!badge) return;
    badge.textContent = value || "-";
  };

  const syncFromStorage = () => {
    const table = getStoredTable();
    setBadge(table);
    if (table) {
      try {
        localStorage.setItem(localTableKey, table);
        sessionStorage.setItem(sessionTableKey, table);
      } catch (_e) {}
    }
  };

  syncFromStorage();

  const showNotice = (message) => {
    if (!message) return;
    const box = document.createElement("div");
    box.textContent = message;
    box.style.position = "fixed";
    box.style.top = "16px";
    box.style.right = "16px";
    box.style.zIndex = "9999";
    box.style.background = "rgba(16,185,129,.95)";
    box.style.color = "#fff";
    box.style.padding = "10px 14px";
    box.style.borderRadius = "10px";
    box.style.boxShadow = "0 8px 22px rgba(0,0,0,.28)";
    box.style.fontSize = ".9rem";
    document.body.appendChild(box);
    window.setTimeout(() => box.remove(), 2600);
  };

  try {
    const notice = sessionStorage.getItem(authNoticeKey) || "";
    if (notice) {
      sessionStorage.removeItem(authNoticeKey);
      showNotice(notice);
    }
  } catch (_e) {}

  const tableModal = tableModalEl ? bootstrap.Modal.getOrCreateInstance(tableModalEl) : null;
  const authModal = authModalEl ? bootstrap.Modal.getOrCreateInstance(authModalEl) : null;
  const bodyMember = document.body?.dataset?.member === "true";
  const customerOrdersStorageKey = "nr_customer_orders_v1";
  const customerCartStorageKey = "nr_customer_cart_v1";

  const clearGuestOrderHistoryForTable = (tableNumber) => {
    const t = Number.parseInt(String(tableNumber || ""), 10);
    if (!t || t <= 0) return;
    try {
      const raw = localStorage.getItem(customerOrdersStorageKey);
      const parsed = raw ? JSON.parse(raw) : {};
      if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
        delete parsed[String(t)];
        localStorage.setItem(customerOrdersStorageKey, JSON.stringify(parsed));
      }
    } catch (_e) {}
    try {
      localStorage.removeItem(`nr_lastCallId_${t}`);
    } catch (_e) {}
  };

  const clearGuestSessionData = (previousTableNumber, nextTableNumber) => {
    if (bodyMember) return;
    clearGuestOrderHistoryForTable(previousTableNumber);
    clearGuestOrderHistoryForTable(nextTableNumber);
    try {
      localStorage.removeItem(customerCartStorageKey);
    } catch (_e) {}
  };

  const tryRestorePendingMemberTable = async () => {
    if (!bodyMember) return;
    if (isTableLocked()) return;
    try {
      const pendingRaw = localStorage.getItem(pendingMemberTableKey) || "";
      const pendingTable = Number.parseInt(pendingRaw, 10);
      if (!pendingTable || pendingTable <= 0) return;

      const current = Number.parseInt(getStoredTable() || "", 10) || null;
      const res = await fetch("/api/tables/select", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ tableNumber: pendingTable, previousTableNumber: current })
      });
      const data = await res.json();
      if (res.ok && data && data.success) {
        const clean = String(pendingTable);
        setStoredTable(clean);
        setBadge(clean);
        try {
          localStorage.setItem(localTableKey, clean);
          sessionStorage.setItem(sessionTableKey, clean);
        } catch (_e) {}
        showNotice(`Login member berhasil. Meja ${clean} dipilih.`);
      }
    } catch (_e) {
    } finally {
      try { localStorage.removeItem(pendingMemberTableKey); } catch (_e) {}
    }
  };

  if (bodyMember && membershipMember && membershipGuest) {
    membershipMember.checked = true;
    membershipGuest.checked = false;
  }

  tryRestorePendingMemberTable();

  let tableRows = [];
  let selectedTableNumber = Number.parseInt(getStoredTable() || "", 10) || null;

  const setHint = (message, isError) => {
    if (!tableHintEl) return;
    tableHintEl.textContent = message;
    tableHintEl.style.color = isError ? "#fca5a5" : "rgba(255,255,255,0.55)";
  };

  const renderTablePicker = () => {
    if (!tableGridEl) return;
    tableGridEl.innerHTML = "";
    tableRows.forEach((row) => {
      const status = String(row.status || "Kosong");
      const statusKey = status.toLowerCase();
      const card = document.createElement("button");
      card.type = "button";
      card.className = `table-picker-card status-${statusKey}`;
      card.dataset.table = String(row.tableNumber);
      card.dataset.status = status;
      const isSelectable = statusKey === "kosong" || selectedTableNumber === row.tableNumber;
      if (!isSelectable) {
        card.disabled = true;
      }
      if (selectedTableNumber === row.tableNumber) {
        card.classList.add("selected");
      }
      card.innerHTML = `<div class="tp-number">Meja ${row.tableNumber}</div><div class="tp-status">${status}</div>`;
      card.addEventListener("click", () => {
        selectedTableNumber = row.tableNumber;
        if (input) input.value = String(row.tableNumber);
        renderTablePicker();
        setHint(`Meja ${row.tableNumber} dipilih. Klik \"Booking Meja\" untuk simpan.`, false);
      });
      tableGridEl.appendChild(card);
    });

    if (!tableRows.length) {
      setHint("Data meja belum tersedia.", true);
    }
  };

  const fetchTables = async () => {
    const response = await fetch("/api/tables", { cache: "no-store" });
    if (!response.ok) {
      throw new Error("failed");
    }
    const data = await response.json();
    tableRows = (data && data.tables) || [];
    renderTablePicker();
  };

  if (tableModalEl && tableModal) {
    tableModalEl.addEventListener("show.bs.modal", async () => {
      if (isTableLocked()) {
        tableModal.hide();
        return;
      }
      const saved = Number.parseInt(getStoredTable() || "", 10);
      selectedTableNumber = Number.isNaN(saved) ? null : saved;
      if (input) {
        input.value = selectedTableNumber ? String(selectedTableNumber) : "";
      }
      setHint("Pilih meja yang statusnya kosong.", false);
      try {
        await fetchTables();
      } catch (_e) {
        setHint("Gagal memuat status meja.", true);
      }
    });
  }

  if (saveBtn && tableModal) {
    saveBtn.addEventListener("click", async () => {
      if (isTableLocked()) {
        setHint("Nomor meja dari QR sudah terkunci.", true);
        return;
      }
      if (!selectedTableNumber || selectedTableNumber <= 0) {
        setHint("Pilih salah satu meja terlebih dahulu.", true);
        return;
      }

      const memberSelected = document.getElementById("membershipMember");
      const requiresMemberAuth = Boolean(memberSelected && memberSelected.checked && !bodyMember);
      if (requiresMemberAuth) {
        setHint("Login/register member dulu sebelum booking meja.", false);
        try {
          localStorage.setItem(pendingMemberTableKey, String(selectedTableNumber));
        } catch (_e) {}
        tableModal.hide();
        if (authModal) {
          window.setTimeout(() => authModal.show(), 150);
        }
        return;
      }

      const previousTable = Number.parseInt(getStoredTable() || "", 10);
      saveBtn.disabled = true;
      try {
        const res = await fetch("/api/tables/select", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ tableNumber: selectedTableNumber, previousTableNumber: previousTable || null })
        });
        const data = await res.json();
        if (!data || !data.success) {
          setHint((data && data.error) || "Meja tidak bisa dibooking sekarang.", true);
          await fetchTables();
          return;
        }

        clearGuestSessionData(previousTable || null, selectedTableNumber);
        const clean = String(selectedTableNumber);
        setStoredTable(clean);
        setBadge(clean);
        try {
          localStorage.setItem(localTableKey, clean);
          sessionStorage.setItem(sessionTableKey, clean);
        } catch (_e) {}
        tableModal.hide();
        startCustomerAlertPolling();
      } catch (_e) {
        setHint("Gagal menyimpan booking meja.", true);
      } finally {
        saveBtn.disabled = false;
      }
    });
  }

  if (resetBtn) {
    resetBtn.addEventListener("click", async () => {
      if (isTableLocked()) {
        setHint("Nomor meja dari QR sudah terkunci.", true);
        return;
      }
      const current = Number.parseInt(getStoredTable() || "", 10);
      if (current > 0) {
        try {
          await fetch("/api/tables/release", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ tableNumber: current })
          });
        } catch (_e) {
        }
      }
      clearGuestSessionData(current || null, null);
      clearStoredTable();
      setBadge("-");
      selectedTableNumber = null;
      if (input) input.value = "";
      setHint("Booking meja sudah direset.", false);
      try {
        await fetchTables();
      } catch (_e) {
      }
    });
  }

  const authContinueBtn = document.getElementById("authContinueBtn");
  const authError = document.getElementById("authError");
  const authModalLabel = document.getElementById("authModalLabel");
  const authModalSubtitle = document.getElementById("authModalSubtitle");
  const authTab = document.getElementById("authTab");
  const authLoginTabWrap = document.getElementById("authLoginTabWrap");
  const authRegisterTabWrap = document.getElementById("authRegisterTabWrap");
  const registerPane = document.getElementById("register-pane");
  const loginTabBtn = document.getElementById("login-tab");
  const loginUsername = document.getElementById("loginUsername");
  const loginPassword = document.getElementById("loginPassword");
  const loginPasswordToggle = document.getElementById("loginPasswordToggle");
  const regName = document.getElementById("regName");
  const regUsername = document.getElementById("regUsername");
  const regPassword = document.getElementById("regPassword");
  const regPasswordToggle = document.getElementById("regPasswordToggle");

  const setAuthError = (message) => {
    if (!authError) {
      return;
    }
    authError.textContent = message || "";
    authError.classList.toggle("d-none", !message);
  };

  const setInvalid = (element, isInvalid) => {
    if (!element) {
      return;
    }
    element.classList.toggle("is-invalid", Boolean(isInvalid));
  };

  const postJson = async (url, payload) => {
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    return response.json();
  };

  const bindPasswordToggle = (inputEl, toggleEl) => {
    if (!inputEl || !toggleEl) return;
    toggleEl.addEventListener("click", () => {
      const show = inputEl.type === "password";
      inputEl.type = show ? "text" : "password";
      toggleEl.setAttribute("aria-label", show ? "Sembunyikan password" : "Tampilkan password");
      toggleEl.setAttribute("aria-pressed", show ? "true" : "false");
      const icon = toggleEl.querySelector("i");
      if (icon) {
        icon.className = show ? "bi bi-eye-slash" : "bi bi-eye";
      }
    });
  };

  const resetPasswordToggleState = (inputEl, toggleEl) => {
    if (!inputEl || !toggleEl) return;
    inputEl.type = "password";
    toggleEl.setAttribute("aria-label", "Tampilkan password");
    toggleEl.setAttribute("aria-pressed", "false");
    const icon = toggleEl.querySelector("i");
    if (icon) {
      icon.className = "bi bi-eye";
    }
  };

  bindPasswordToggle(loginPassword, loginPasswordToggle);
  bindPasswordToggle(regPassword, regPasswordToggle);

  if (authModalEl) {
    authModalEl.addEventListener("show.bs.modal", function (event) {
      if (loginUsername) {
        loginUsername.value = "";
      }
      if (loginPassword) {
        loginPassword.value = "";
      }
      if (regPassword) {
        regPassword.value = "";
      }
      resetPasswordToggleState(loginPassword, loginPasswordToggle);
      resetPasswordToggleState(regPassword, regPasswordToggle);

      const trigger = event.relatedTarget;
      const isStaffMode = trigger && trigger.dataset && trigger.dataset.authMode === "staff";
      if (isStaffMode) {
        if (authModalLabel) authModalLabel.textContent = "Login Pegawai";
        if (authModalSubtitle) authModalSubtitle.textContent = "Masuk untuk akses dashboard Admin/Kasir/Dapur.";
        if (authRegisterTabWrap) authRegisterTabWrap.classList.add("d-none");
        if (registerPane) registerPane.classList.add("d-none");
        if (authTab) authTab.classList.add("staff-login-only");
        if (authLoginTabWrap) authLoginTabWrap.classList.remove("w-50");
        if (loginTabBtn) bootstrap.Tab.getOrCreateInstance(loginTabBtn).show();
      } else {
        if (authModalLabel) authModalLabel.textContent = "Akses Member";
        if (authModalSubtitle) authModalSubtitle.textContent = "Masuk atau daftar untuk lanjut sebagai member.";
        if (authRegisterTabWrap) authRegisterTabWrap.classList.remove("d-none");
        if (registerPane) registerPane.classList.remove("d-none");
        if (authTab) authTab.classList.remove("staff-login-only");
        if (authLoginTabWrap) authLoginTabWrap.classList.add("w-50");
      }
    });
  }

  if (authContinueBtn && authModal) {
    authContinueBtn.addEventListener("click", async () => {
      setAuthError("");

      const params = new URLSearchParams(window.location.search);
      const returnUrl = params.get("ReturnUrl") || "";
      const activeTab = document.querySelector("#authTab .nav-link.active");
      const isLogin = !activeTab || activeTab.id === "login-tab";

      if (isLogin) {
        const usernameValue = loginUsername ? loginUsername.value.trim() : "";
        const passwordValue = loginPassword ? loginPassword.value : "";

        setInvalid(loginUsername, !usernameValue);
        setInvalid(loginPassword, !passwordValue);

        if (!usernameValue || !passwordValue) {
          return;
        }

        authContinueBtn.disabled = true;
        try {
          const data = await postJson("/Auth/AjaxLogin", {
            username: usernameValue,
            password: passwordValue,
            returnUrl,
          });

          if (!data || !data.success) {
            setAuthError((data && data.error) || "Login gagal.");
            return;
          }

          window.location.href = data.redirectUrl || "/";
        } catch (error) {
          setAuthError("Gagal menghubungi server.");
        } finally {
          authContinueBtn.disabled = false;
        }
        return;
      }

      const fullNameValue = regName ? regName.value.trim() : "";
      const usernameValue = regUsername ? regUsername.value.trim() : "";
      const passwordValue = regPassword ? regPassword.value : "";

      setInvalid(regName, !fullNameValue);
      setInvalid(regUsername, !usernameValue);
      setInvalid(regPassword, !passwordValue);

      if (!fullNameValue || !usernameValue || !passwordValue) {
        return;
      }

      authContinueBtn.disabled = true;
      try {
        const data = await postJson("/Auth/AjaxRegister", {
          fullName: fullNameValue,
          username: usernameValue,
          password: passwordValue,
        });

        if (!data || !data.success) {
          setAuthError((data && data.error) || "Registrasi gagal.");
          return;
        }

        try {
          sessionStorage.setItem(authNoticeKey, "Akun berhasil dibuat. Kamu sudah login sebagai member.");
        } catch (_e) {}
        window.location.href = data.redirectUrl || "/";
      } catch (error) {
        setAuthError("Gagal menghubungi server.");
      } finally {
        authContinueBtn.disabled = false;
      }
    });
  }

  try {
    const heroBtn = document.getElementById("heroPickTable");
    if (heroBtn) {
      heroBtn.addEventListener("click", function (e) {
        e.preventDefault();
        const modalEl = document.getElementById("tableModal");
        if (modalEl) {
          bootstrap.Modal.getOrCreateInstance(modalEl).show();
        } else {
          document.getElementById("pickTableBtn")?.click();
        }
      });
    }
  } catch (err) {
    console && console.warn && console.warn("heroPickTable binding failed", err);
  }

  // Polling alarm for customer table alerts.
  const alertModalEl = document.getElementById("tableReadyModal");
  const readyTableEl = document.getElementById("tableReadyNumber");
  const readyMessageEl = document.getElementById("tableReadyMessage");
  const stopAlertBtn = document.getElementById("stopTableAlertBtn");
  const alertModal = alertModalEl ? bootstrap.Modal.getOrCreateInstance(alertModalEl) : null;

  let audioCtx = null;
  let alarmTimer = null;
  let pollingTimer = null;

  const stopAlarm = () => {
    if (alarmTimer) {
      window.clearInterval(alarmTimer);
      alarmTimer = null;
    }
    if (audioCtx && typeof audioCtx.close === "function") {
      audioCtx.close().catch(() => { });
      audioCtx = null;
    }
  };

  const beepOnce = () => {
    if (!window.AudioContext && !window.webkitAudioContext) {
      return;
    }

    if (!audioCtx) {
      const Ctx = window.AudioContext || window.webkitAudioContext;
      audioCtx = new Ctx();
    }

    const now = audioCtx.currentTime;
    const gain = audioCtx.createGain();
    gain.connect(audioCtx.destination);
    gain.gain.setValueAtTime(0.0001, now);
    gain.gain.exponentialRampToValueAtTime(0.15, now + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.25);

    const osc = audioCtx.createOscillator();
    osc.type = "triangle";
    osc.frequency.setValueAtTime(880, now);
    osc.connect(gain);
    osc.start(now);
    osc.stop(now + 0.26);
  };

  const startAlarm = () => {
    stopAlarm();
    beepOnce();
    alarmTimer = window.setInterval(beepOnce, 900);

    if (navigator.vibrate) {
      navigator.vibrate([300, 120, 250, 120, 250]);
    }
  };

  if (stopAlertBtn) {
    stopAlertBtn.addEventListener("click", stopAlarm);
  }
  if (alertModalEl) {
    alertModalEl.addEventListener("hidden.bs.modal", stopAlarm);
  }

  const startCustomerAlertPolling = () => {
    if (!alertModal || !readyTableEl || !readyMessageEl) {
      return;
    }

    const tableStr = getStoredTable();
    const tableNumber = Number.parseInt(tableStr, 10);
    if (!tableNumber || tableNumber <= 0) {
      return;
    }

    const storageLastIdKey = `nr_lastCallId_${tableNumber}`;
    let isFetching = false;

    if (pollingTimer) {
      window.clearInterval(pollingTimer);
      pollingTimer = null;
    }

    const showAlert = (id, message) => {
      localStorage.setItem(storageLastIdKey, String(id));
      readyTableEl.textContent = String(tableNumber);
      readyMessageEl.textContent = message || "Pesanan Anda sudah siap.";
      alertModal.show();
      startAlarm();
    };

    const poll = async () => {
      if (isFetching) return;
      isFetching = true;
      try {
        const lastId = Number.parseInt(localStorage.getItem(storageLastIdKey) || "0", 10) || 0;
        const response = await fetch(`/api/table-alerts/latest?tableNumber=${tableNumber}&afterId=${lastId}`, {
          cache: "no-store",
        });

        if (!response.ok) {
          return;
        }

        const data = await response.json();
        if (data && data.success && data.hasAlert && data.id) {
          showAlert(data.id, data.message);
        }
      } catch (_err) {
      } finally {
        isFetching = false;
      }
    };

    const bootstrapLastId = async () => {
      const existing = Number.parseInt(localStorage.getItem(storageLastIdKey) || "0", 10) || 0;
      if (existing > 0) {
        return;
      }

      try {
        const response = await fetch(`/api/table-alerts/latest?tableNumber=${tableNumber}`, {
          cache: "no-store",
        });
        if (!response.ok) {
          return;
        }
        const data = await response.json();
        if (data && data.success && data.hasAlert && data.id) {
          // Baseline only: existing historical alert should not ring immediately on new table selection.
          localStorage.setItem(storageLastIdKey, String(data.id));
        }
      } catch (_err) {
      }
    };

    bootstrapLastId().finally(() => {
      poll();
      pollingTimer = window.setInterval(poll, 5000);
    });
  };

  startCustomerAlertPolling();
});
