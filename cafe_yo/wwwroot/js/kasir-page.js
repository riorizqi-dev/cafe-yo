document.addEventListener("DOMContentLoaded", () => {
            const callButtons = Array.from(document.querySelectorAll(".kasir-call-btn"));
            const tableCompleteButtons = Array.from(document.querySelectorAll(".kasir-complete-btn"));
            const stateButtons = Array.from(document.querySelectorAll(".kasir-state-btn"));
            const counterEl = document.getElementById("kasirCallCounter");
            const messageInput = document.getElementById("globalCallMessage");
            const kitchenNotifCount = document.getElementById("kitchenNotifCount");
            const kitchenNotifList = document.getElementById("kitchenNotifList");
            const orderTableNumber = document.getElementById("orderTableNumber");
            const orderNote = document.getElementById("orderNote");
            const orderPaymentMethod = document.getElementById("orderPaymentMethod");
            const kasirMenuPicker = document.getElementById("kasirMenuPicker");
            const orderItemsWrap = document.getElementById("orderItemsWrap");
            const submitOrderBtn = document.getElementById("submitOrderBtn");
            const paymentModalEl = document.getElementById("orderPaymentModal");
            const paymentModal = paymentModalEl ? bootstrap.Modal.getOrCreateInstance(paymentModalEl) : null;
            const paymentOrderLabel = document.getElementById("paymentOrderLabel");
            const paymentTableLabel = document.getElementById("paymentTableLabel");
            const paymentTotalLabel = document.getElementById("paymentTotalLabel");
            const paymentFeeLabel = document.getElementById("paymentFeeLabel");
            const paymentPayableLabel = document.getElementById("paymentPayableLabel");
            const paymentStatusBadge = document.getElementById("paymentStatusBadge");
            const paymentInvoiceText = document.getElementById("paymentInvoiceText");
            const paymentQrImage = document.getElementById("paymentQrImage");
            const paymentQrFallback = document.getElementById("paymentQrFallback");
            const paymentMessageText = document.getElementById("paymentMessageText");
            const refreshPaymentBtn = document.getElementById("refreshPaymentBtn");
            const openCheckoutBtn = document.getElementById("openCheckoutBtn");
            const copyInvoiceBtn = document.getElementById("copyInvoiceBtn");
            const paymentMethodFilter = document.getElementById("paymentMethodFilter");
            const refreshPendingPaymentsBtn = document.getElementById("refreshPendingPaymentsBtn");
            const pendingPaymentsBody = document.getElementById("pendingPaymentsBody");
            const receiptModalEl = document.getElementById("kasirReceiptModal");
            const receiptModal = receiptModalEl ? bootstrap.Modal.getOrCreateInstance(receiptModalEl) : null;
            const receiptContent = document.getElementById("kasirReceiptContent");
            const printReceiptBtn = document.getElementById("printReceiptBtn");
            const completeTableModalEl = document.getElementById("completeTableModal");
            const completeTableModal = completeTableModalEl ? bootstrap.Modal.getOrCreateInstance(completeTableModalEl) : null;
            const completeTableModalMessage = document.getElementById("completeTableModalMessage");
            const confirmCompleteTableBtn = document.getElementById("confirmCompleteTableBtn");
            const kasirSectionNav = document.getElementById("kasirSectionNav");
            const kasirNavButtons = Array.from(document.querySelectorAll(".kasir-nav-btn"));
            const kasirPanels = Array.from(document.querySelectorAll(".kasir-panel"));
            const menuOptionsJsonEl = document.getElementById("kasirMenuOptionsJson");
            let menuOptions = [];
            try {
                menuOptions = JSON.parse(menuOptionsJsonEl?.textContent || "[]");
            } catch (_e) {
                menuOptions = [];
            }
            const menuById = new Map((Array.isArray(menuOptions) ? menuOptions : []).map(m => [Number(m.id), m]));
            const selectedOrderItems = new Map();
            let count = 0;
            let activePaymentOrderId = 0;
            let activePaymentInvoice = "";
            let activePaymentTableNumber = 0;
            let activePaymentTotal = 0;
            let activePaymentGatewayFee = 0;
            let activePaymentPayableTotal = 0;
            let activePaymentUrl = "";
            let activePaymentQrString = "";
            let activePaymentQrImageUrl = "";
            let pendingCompleteTableNumber = 0;
            const shownKitchenNotif = new Set();

            const switchKasirPanel = (panelKey) => {
                const next = String(panelKey || "order").trim().toLowerCase();
                kasirNavButtons.forEach((btn) => {
                    const isActive = String(btn.dataset.kasirTab || "") === next;
                    btn.classList.toggle("is-active", isActive);
                });
                kasirPanels.forEach((panel) => {
                    const isActive = String(panel.dataset.kasirPanel || "") === next;
                    panel.classList.toggle("is-active", isActive);
                });
            };

            const toastContainer = document.createElement("div");
            toastContainer.className = "position-fixed top-0 end-0 p-3";
            toastContainer.style.zIndex = "1090";
            document.body.appendChild(toastContainer);

            const showToast = (title, body, ok) => {
                const toastEl = document.createElement("div");
                toastEl.className = "toast align-items-center border-0 text-bg-" + (ok ? "success" : "danger");
                toastEl.setAttribute("role", "alert");
                toastEl.innerHTML = `
                  <div class="d-flex">
                    <div class="toast-body"><strong>${title}</strong><div>${body}</div></div>
                    <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
                  </div>`;
                toastContainer.appendChild(toastEl);
                const toast = bootstrap.Toast.getOrCreateInstance(toastEl, { delay: 2500 });
                toastEl.addEventListener("hidden.bs.toast", () => toastEl.remove());
                toast.show();
            };

            const buildMessage = (tableNumber) => {
                const template = (messageInput && messageInput.value ? messageInput.value.trim() : "");
                if (!template) {
                    return "Pesanan Anda sudah siap. Silakan ke meja " + tableNumber + ".";
                }
                return template.replaceAll("{table}", tableNumber);
            };

            const formatRupiah = (value) => {
                try {
                    return new Intl.NumberFormat("id-ID", { style: "currency", currency: "IDR", maximumFractionDigits: 0 }).format(value || 0);
                } catch (_e) {
                    return "Rp " + String(value || 0);
                }
            };

            const escapeHtml = (value) => String(value || "")
                .replaceAll("&", "&amp;")
                .replaceAll("<", "&lt;")
                .replaceAll(">", "&gt;")
                .replaceAll("\"", "&quot;");

            const resolveMenuCategory = (category) => {
                const raw = String(category || "").trim().toLowerCase();
                if (["food", "makanan", "steak"].includes(raw)) return { key: "food", label: "Makanan" };
                if (["drink", "minuman"].includes(raw)) return { key: "drink", label: "Minuman" };
                if (["dessert", "pencuci mulut"].includes(raw)) return { key: "dessert", label: "Pencuci Mulut" };
                if (["snack", "cemilan", "camilan", "jajanan", "dimsum"].includes(raw)) return { key: "jajanan", label: "Jajanan" };
                return { key: raw || "lainnya", label: "Lainnya" };
            };

            const fallbackImageByCategory = (category) => {
                const key = resolveMenuCategory(category).key;
                if (key === "food") return "https://images.unsplash.com/photo-1512621776951-a57141f2eefd?auto=format&fit=crop&w=1200&q=80";
                if (key === "drink") return "https://images.unsplash.com/photo-1461023058943-07fcbe16d735?auto=format&fit=crop&w=1200&q=80";
                if (key === "dessert") return "https://images.unsplash.com/photo-1488477181946-6428a0291777?auto=format&fit=crop&w=1200&q=80";
                if (key === "jajanan") return "https://images.unsplash.com/photo-1473093295043-cdd812d0e601?auto=format&fit=crop&w=1200&q=80";
                return "https://images.unsplash.com/photo-1498837167922-ddd27525d352?auto=format&fit=crop&w=1200&q=80";
            };

            const normalizeStatus = (status) => String(status || "").trim().toLowerCase();
            const normalizePaymentMethodLabel = (value) => {
                const key = String(value || "").trim().toLowerCase();
                if (key === "kasir" || key === "cash" || key === "tunai") return "Tunai (Kasir)";
                if (key === "qris") return "QRIS Daring";
                return value || "-";
            };

            const paymentBadgeClass = (status) => {
                const key = normalizeStatus(status);
                if (key === "paid" || key === "lunas") return "text-bg-success";
                if (key === "pending") return "text-bg-warning";
                if (key === "expired" || key === "cancelled" || key === "canceled") return "text-bg-danger";
                return "text-bg-secondary";
            };

            const buildQrImageUrl = (qrString) =>
                `https://api.qrserver.com/v1/create-qr-code/?size=420x420&data=${encodeURIComponent(qrString || "")}`;

            const renderPaymentResult = ({ orderId, tableNumber, total, gatewayFee, payableTotal, invoice, status, paymentUrl, qrString, qrImageUrl, message }) => {
                const nextOrderId = Number(orderId || 0);
                if (nextOrderId > 0 && nextOrderId !== activePaymentOrderId) {
                    activePaymentUrl = "";
                    activePaymentQrString = "";
                    activePaymentQrImageUrl = "";
                }
                activePaymentOrderId = nextOrderId;
                activePaymentInvoice = String(invoice || "").trim();
                const nextTableNumber = Number.parseInt(String(tableNumber || "0"), 10);
                const nextTotal = Number.parseFloat(String(total || "0"));
                const nextFee = Number.parseFloat(String(gatewayFee || "0"));
                const nextPayable = Number.parseFloat(String(payableTotal || "0"));
                if (nextTableNumber > 0) {
                    activePaymentTableNumber = nextTableNumber;
                }
                if (Number.isFinite(nextTotal) && nextTotal >= 0) {
                    activePaymentTotal = nextTotal;
                }
                if (Number.isFinite(nextFee) && nextFee >= 0) {
                    activePaymentGatewayFee = nextFee;
                }
                if (Number.isFinite(nextPayable) && nextPayable >= 0) {
                    activePaymentPayableTotal = nextPayable;
                }
                if (typeof paymentUrl === "string" && paymentUrl.trim()) {
                    activePaymentUrl = paymentUrl.trim();
                }
                if (typeof qrString === "string" && qrString.trim()) {
                    activePaymentQrString = qrString.trim();
                }
                if (typeof qrImageUrl === "string" && qrImageUrl.trim()) {
                    activePaymentQrImageUrl = qrImageUrl.trim();
                }

                if (paymentOrderLabel) paymentOrderLabel.textContent = `Pesanan #${orderId || "-"}`;
                if (paymentTableLabel) paymentTableLabel.textContent = `Meja ${activePaymentTableNumber || "-"}`;
                if (paymentTotalLabel) paymentTotalLabel.textContent = `Total Pesanan ${formatRupiah(activePaymentTotal || 0)}`;
                if (paymentFeeLabel) paymentFeeLabel.textContent = `Biaya QRIS ${formatRupiah(activePaymentGatewayFee || 0)}`;
                if (paymentPayableLabel) paymentPayableLabel.textContent = `Total Bayar ${formatRupiah(activePaymentPayableTotal || activePaymentTotal || 0)}`;
                if (paymentInvoiceText) paymentInvoiceText.textContent = `Kode Pembayaran: ${activePaymentInvoice || "-"}`;

                if (paymentStatusBadge) {
                    const klass = paymentBadgeClass(status);
                    paymentStatusBadge.className = `badge ${klass}`;
                    paymentStatusBadge.textContent = `Status: ${status || "-"}`;
                }

                if (paymentMessageText) {
                    paymentMessageText.textContent = message || "Silakan pindai QRIS untuk lanjut pembayaran.";
                }

                if (openCheckoutBtn) {
                    if (activePaymentUrl) {
                        openCheckoutBtn.href = activePaymentUrl;
                        openCheckoutBtn.classList.remove("d-none");
                    } else {
                        openCheckoutBtn.removeAttribute("href");
                        openCheckoutBtn.classList.add("d-none");
                    }
                }

                const finalQrImage = activePaymentQrImageUrl || (activePaymentQrString ? buildQrImageUrl(activePaymentQrString) : "");
                if (paymentQrImage && paymentQrFallback) {
                    if (finalQrImage) {
                        paymentQrImage.src = finalQrImage;
                        paymentQrImage.classList.remove("d-none");
                        paymentQrFallback.classList.add("d-none");
                    } else {
                        paymentQrImage.removeAttribute("src");
                        paymentQrImage.classList.add("d-none");
                        paymentQrFallback.classList.remove("d-none");
                    }
                }

                paymentModal?.show();
            };

            const refreshPaymentStatus = async () => {
                if (!activePaymentOrderId) {
                    showToast("Informasi", "Belum ada pembayaran pesanan yang aktif.", false);
                    return;
                }

                if (refreshPaymentBtn) refreshPaymentBtn.disabled = true;
                try {
                    const res = await fetch(`/api/payments/orders/${activePaymentOrderId}/refresh`, { cache: "no-store" });
                    const data = await res.json();
                    if (!res.ok || !data) {
                        showToast("Gagal", (data && data.error) || "Gagal memuat ulang status pembayaran.", false);
                        return;
                    }

                    renderPaymentResult({
                        orderId: activePaymentOrderId,
                        tableNumber: activePaymentTableNumber,
                        total: activePaymentTotal,
                        gatewayFee: data.gatewayFee,
                        payableTotal: data.payableTotal,
                        invoice: data.invoice || activePaymentInvoice,
                        status: data.status,
                        paymentUrl: data.paymentUrl,
                        qrString: data.qrString,
                        qrImageUrl: data.qrImageUrl,
                        message: data.message
                    });

                    if (["paid", "lunas"].includes(normalizeStatus(data.status))) {
                        showToast("Pembayaran Berhasil", `Pesanan #${activePaymentOrderId} sudah dibayar.`, true);
                    } else {
                        showToast("Status Pembayaran", `Pesanan #${activePaymentOrderId}: ${data.status || "-"}.`, true);
                    }
                } catch (_err) {
                    showToast("Kesalahan", "Tidak bisa menghubungi server pembayaran.", false);
                } finally {
                    if (refreshPaymentBtn) refreshPaymentBtn.disabled = false;
                }
            };

            const createOrderPayment = async (orderId, tableNumber, total) => {
                const callbackUrl = `${window.location.origin}/api/payments/gateway/callback`;
                const redirectUrl = window.location.href;
                const payload = {
                    customerName: `Meja ${tableNumber}`,
                    callbackUrl,
                    redirectUrl,
                    paymentMethod: "qris",
                    useQrisConverter: false
                };

                const res = await fetch(`/api/payments/orders/${orderId}/create`, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(payload)
                });
                const data = await res.json();

                if (!data) {
                    throw new Error("Respons pembayaran kosong.");
                }

                if (!res.ok && !data.invoice && !data.qrString && !data.qrImageUrl && !data.paymentUrl) {
                    throw new Error(data.error || data.message || "Gagal membuat pembayaran QRIS.");
                }

                renderPaymentResult({
                    orderId,
                    tableNumber,
                    total,
                    gatewayFee: data.gatewayFee,
                    payableTotal: data.payableTotal,
                    invoice: data.invoice,
                    status: data.status,
                    paymentUrl: data.paymentUrl,
                    qrString: data.qrString,
                    qrImageUrl: data.qrImageUrl,
                    message: data.message || (res.ok ? "Silakan lanjutkan pembayaran." : "Pembayaran dibuat tetapi respons gateway tidak standar.")
                });
            };

            const collectOrderItems = () => {
                return Array.from(selectedOrderItems.values())
                    .map((item) => ({
                        menuItemId: Number(item.menuItemId || 0),
                        menuName: String(item.menuName || ""),
                        quantity: Number(item.quantity || 0),
                        notes: String(item.notes || "").trim()
                    }))
                    .filter((x) => x.menuItemId > 0 && x.quantity > 0);
            };

            const renderSelectedOrderItems = () => {
                if (!orderItemsWrap) return;
                if (selectedOrderItems.size === 0) {
                    orderItemsWrap.innerHTML = '<div class="text-white-50 small">Belum ada item dipilih. Klik tombol "Tambah" pada menu.</div>';
                    return;
                }
                const rows = Array.from(selectedOrderItems.values()).map((item) => `
                    <div class="row g-2 order-item-row" data-menu-id="${item.menuItemId}">
                        <div class="col-md-4">
                            <div class="form-control order-item-static">${escapeHtml(item.menuName)}</div>
                        </div>
                        <div class="col-md-2">
                            <input class="form-control order-item-qty" type="number" min="1" value="${Number(item.quantity || 1)}" />
                        </div>
                        <div class="col-md-5">
                            <input class="form-control order-item-note" maxlength="250" placeholder="Catatan item" value="${escapeHtml(item.notes || "")}" />
                        </div>
                        <div class="col-md-1 d-grid">
                            <button class="btn btn-outline-danger remove-order-item-btn" type="button">&times;</button>
                        </div>
                    </div>`).join("");
                orderItemsWrap.innerHTML = rows;
            };

            const renderKasirMenuPicker = () => {
                if (!kasirMenuPicker) return;
                const availableItems = Array.isArray(menuOptions) ? menuOptions.filter((x) => x.available) : [];
                if (!availableItems.length) {
                    kasirMenuPicker.innerHTML = '<div class="alert alert-warning w-100 mb-0">Tidak ada menu aktif untuk diorder.</div>';
                    return;
                }

                kasirMenuPicker.innerHTML = availableItems.map((m) => {
                    const cat = resolveMenuCategory(m.category);
                    const img = m.imageUrl || fallbackImageByCategory(m.category);
                    const desc = String(m.description || "Menu favorit hari ini").trim() || "Menu favorit hari ini";
                    return `
                        <article class="menu-card-2 kasir-menu-card"
                                 data-id="${m.id}"
                                 data-name="${escapeHtml(m.name)}"
                                 data-price="${Number(m.price || 0)}">
                            <div class="menu-image">
                                <img src="${escapeHtml(img)}" alt="${escapeHtml(m.name)}" loading="lazy"
                                     onerror="this.onerror=null;this.src='${escapeHtml(fallbackImageByCategory(m.category))}';" />
                            </div>
                            <div class="menu-body">
                                <div class="menu-chip">${cat.label}</div>
                                <div class="menu-title">${escapeHtml(m.name)}</div>
                                <div class="menu-desc">${escapeHtml(desc)}</div>
                                <div class="menu-footer">
                                    <div class="menu-price">${formatRupiah(Number(m.price || 0))}</div>
                                    <button class="btn btn-add kasir-add-menu-btn" type="button">Tambah</button>
                                </div>
                            </div>
                        </article>`;
                }).join("");
            };

            const triggerCardFx = (tableNumber) => {
                const card = document.querySelector(`.kasir-card[data-table="${tableNumber}"]`);
                if (!card) return;
                card.classList.remove("is-called");
                void card.offsetWidth;
                card.classList.add("is-called");
                card.scrollIntoView({ behavior: "smooth", block: "center" });
            };

            const applyStatusVisual = (tableNumber, status) => {
                const card = document.querySelector(`.kasir-card[data-table="${tableNumber}"]`);
                if (!card) return;
                card.dataset.status = status;
                const statusEl = card.querySelector(".kasir-status");
                if (statusEl) statusEl.textContent = status;
                card.classList.remove("status-kosong", "status-booking", "status-isi");
                const key = String(status || "").toLowerCase();
                if (key === "booking") card.classList.add("status-booking");
                else if (key === "isi") card.classList.add("status-isi");
                else card.classList.add("status-kosong");
            };

            const executeCompleteTable = async (tableNumber, sourceButton = null) => {
                if (!tableNumber || tableNumber <= 0) {
                    showToast("Gagal", "Nomor meja tidak valid.", false);
                    return;
                }
                if (sourceButton) sourceButton.disabled = true;
                if (confirmCompleteTableBtn) confirmCompleteTableBtn.disabled = true;
                try {
                    const res = await fetch("/Kasir/CompleteTable", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ tableNumber })
                    });
                    const data = await res.json();
                    if (!res.ok || !data || !data.success) {
                        showToast("Gagal", (data && data.error) || "Gagal menyelesaikan order meja.", false);
                        return;
                    }

                    const status = data.status || "Kosong";
                    applyStatusVisual(tableNumber, status);
                    await refreshTablesFromApi();
                    showToast(
                        "Berhasil",
                        `Meja ${tableNumber} -> ${status}. ${data.completedOrders || 0} order aktif diselesaikan.`,
                        true
                    );
                    completeTableModal?.hide();
                } catch (_err) {
                    showToast("Kesalahan", "Tidak bisa menghubungi server.", false);
                } finally {
                    if (sourceButton) sourceButton.disabled = false;
                    if (confirmCompleteTableBtn) confirmCompleteTableBtn.disabled = false;
                }
            };

            document.querySelectorAll(".kasir-card").forEach((card) => {
                applyStatusVisual(card.dataset.table, card.dataset.status || "Kosong");
            });

            const refreshTablesFromApi = async () => {
                try {
                    const res = await fetch("/api/tables", { cache: "no-store" });
                    if (!res.ok) return;
                    const data = await res.json();
                    const tables = Array.isArray(data?.tables) ? data.tables : [];
                    const statusByTable = new Map(tables.map(t => [Number(t.tableNumber), t.status || "Kosong"]));

                    document.querySelectorAll(".kasir-card").forEach((card) => {
                        const tableNumber = Number.parseInt(card.dataset.table || "0", 10);
                        if (!tableNumber) return;
                        const status = statusByTable.get(tableNumber) || card.dataset.status || "Kosong";
                        applyStatusVisual(tableNumber, status);
                    });

                    if (orderTableNumber) {
                        const current = orderTableNumber.value;
                        orderTableNumber.innerHTML = tables
                            .sort((a, b) => Number(a.tableNumber || 0) - Number(b.tableNumber || 0))
                            .map((t) => `<option value="${t.tableNumber}">Meja ${t.tableNumber} (${t.status || "Kosong"})</option>`)
                            .join("");
                        if (current) {
                            orderTableNumber.value = current;
                        }
                    }
                } catch (_e) {
                }
            };

            if (orderItemsWrap && submitOrderBtn) {
                renderKasirMenuPicker();
                renderSelectedOrderItems();

                if (!kasirMenuPicker || kasirMenuPicker.querySelectorAll(".kasir-menu-card").length === 0) {
                    submitOrderBtn.disabled = true;
                    showToast("Informasi", "Tidak ada menu aktif untuk dipesan.", false);
                } else {
                    kasirMenuPicker.addEventListener("click", (event) => {
                        const btn = event.target.closest(".kasir-add-menu-btn");
                        if (!btn) return;
                        const card = btn.closest(".kasir-menu-card");
                        const menuItemId = Number.parseInt(card?.dataset.id || "0", 10);
                        if (!menuItemId || !menuById.has(menuItemId)) {
                            showToast("Gagal", "Item menu tidak valid.", false);
                            return;
                        }
                        const current = selectedOrderItems.get(menuItemId);
                        if (current) {
                            current.quantity += 1;
                            selectedOrderItems.set(menuItemId, current);
                        } else {
                            selectedOrderItems.set(menuItemId, {
                                menuItemId,
                                menuName: menuById.get(menuItemId)?.name || "Menu",
                                quantity: 1,
                                notes: ""
                            });
                        }
                        renderSelectedOrderItems();
                    });

                    orderItemsWrap.addEventListener("click", (event) => {
                        const btn = event.target.closest(".remove-order-item-btn");
                        if (!btn) return;
                        const row = btn.closest(".order-item-row");
                        const menuItemId = Number.parseInt(row?.dataset.menuId || "0", 10);
                        if (menuItemId > 0) selectedOrderItems.delete(menuItemId);
                        renderSelectedOrderItems();
                    });

                    orderItemsWrap.addEventListener("input", (event) => {
                        const row = event.target.closest(".order-item-row");
                        const menuItemId = Number.parseInt(row?.dataset.menuId || "0", 10);
                        if (!menuItemId || !selectedOrderItems.has(menuItemId)) return;
                        const item = selectedOrderItems.get(menuItemId);
                        if (!item) return;
                        const qtyEl = row.querySelector(".order-item-qty");
                        const noteEl = row.querySelector(".order-item-note");
                        const qty = Number.parseInt(qtyEl?.value || "1", 10);
                        item.quantity = Number.isFinite(qty) && qty > 0 ? qty : 1;
                        item.notes = String(noteEl?.value || "").trim();
                        selectedOrderItems.set(menuItemId, item);
                    });

                    submitOrderBtn.addEventListener("click", async () => {
                        const tableNumber = Number.parseInt(orderTableNumber?.value || "0", 10);
                        const items = collectOrderItems();
                        const note = (orderNote?.value || "").trim();
                        const paymentMethod = String(orderPaymentMethod?.value || "kasir").trim().toLowerCase() === "qris" ? "qris" : "kasir";

                        if (!tableNumber) {
                            showToast("Gagal", "Pilih meja terlebih dulu.", false);
                            return;
                        }
                        if (!items.length) {
                            showToast("Gagal", "Tambahkan minimal 1 item pesanan.", false);
                            return;
                        }
                        if (items.some(x => !menuById.has(x.menuItemId))) {
                            showToast("Gagal", "Ada item menu yang tidak valid. Muat ulang halaman.", false);
                            return;
                        }

                        console.debug("Kasir submit order items:", items);

                        submitOrderBtn.disabled = true;
                        try {
                            const payload = {
                                tableNumber,
                                note,
                                paymentMethod,
                                items: items.map(x => ({ menuItemId: x.menuItemId, quantity: x.quantity, notes: x.notes }))
                            };
                            console.debug("Kasir payload:", payload);

                            const res = await fetch("/Kasir/CreateOrder", {
                                method: "POST",
                                headers: { "Content-Type": "application/json" },
                                body: JSON.stringify(payload)
                            });
                            const data = await res.json();
                            if (!res.ok || !data || !data.success) {
                                showToast("Gagal", (data && data.error) || "Pesanan gagal disimpan.", false);
                                return;
                            }

                            applyStatusVisual(tableNumber, "Isi");
                            triggerCardFx(tableNumber);
                            showToast("Berhasil", `Pesanan #${data.orderId} masuk ke dapur.`, true);

                            if (paymentMethod === "qris") {
                                try {
                                    const total = Number.parseFloat(String(data.total || "0"));
                                    await createOrderPayment(data.orderId, tableNumber, Number.isFinite(total) ? total : 0);
                                } catch (payErr) {
                                    showToast("Pembayaran", payErr?.message || "Pesanan tersimpan, tetapi gagal membuat QRIS.", false);
                                }
                            }

                            if (orderNote) orderNote.value = "";
                            selectedOrderItems.clear();
                            renderSelectedOrderItems();
                            await refreshTablesFromApi();
                            await fetchPendingPayments();
                        } catch (_e) {
                            showToast("Kesalahan", "Tidak bisa menghubungi server.", false);
                        } finally {
                            submitOrderBtn.disabled = false;
                        }
                    });
                }
            }

            callButtons.forEach((btn) => {
                btn.addEventListener("click", async () => {
                    const tableNumber = Number.parseInt(btn.dataset.table || "", 10);
                    if (!tableNumber || tableNumber <= 0) {
                        showToast("Gagal", "Nomor meja tidak valid.", false);
                        return;
                    }

                    const message = buildMessage(tableNumber);
                    btn.disabled = true;
                    try {
                        const response = await fetch("/Kasir/CallReady", {
                            method: "POST",
                            headers: { "Content-Type": "application/json" },
                            body: JSON.stringify({ tableNumber, message })
                        });
                        const data = await response.json();
                        if (!data || !data.success) {
                            showToast("Gagal", (data && data.error) || "Panggilan gagal dikirim.", false);
                            return;
                        }

                        count += 1;
                        if (counterEl) {
                            counterEl.textContent = String(count);
                        }
                        triggerCardFx(tableNumber);
                        showToast("Panggilan terkirim", "Notifikasi meja " + tableNumber + " berhasil dikirim.", true);
                    } catch (err) {
                        showToast("Kesalahan", "Tidak bisa menghubungi server.", false);
                    } finally {
                        btn.disabled = false;
                    }
                });
            });

            stateButtons.forEach((btn) => {
                btn.addEventListener("click", async () => {
                    const tableNumber = Number.parseInt(btn.dataset.table || "", 10);
                    const status = btn.dataset.status || "Kosong";
                    if (!tableNumber) return;
                    btn.disabled = true;
                    try {
                        const res = await fetch("/api/tables/status", {
                            method: "POST",
                            headers: { "Content-Type": "application/json" },
                            body: JSON.stringify({ tableNumber, status })
                        });
                        const data = await res.json();
                        if (!data || !data.success) {
                            showToast("Gagal", (data && data.error) || "Status gagal diubah.", false);
                            return;
                        }
                        applyStatusVisual(tableNumber, data.status || status);
                        await refreshTablesFromApi();
                        showToast("Status meja diupdate", `Meja ${tableNumber} => ${data.status || status}.`, true);
                    } catch (_e) {
                        showToast("Kesalahan", "Gagal menghubungi server.", false);
                    } finally {
                        btn.disabled = false;
                    }
                });
            });

            tableCompleteButtons.forEach((btn) => {
                btn.addEventListener("click", async () => {
                    const tableNumber = Number.parseInt(btn.dataset.table || "", 10);
                    if (!tableNumber || tableNumber <= 0) {
                        showToast("Gagal", "Nomor meja tidak valid.", false);
                        return;
                    }
                    pendingCompleteTableNumber = tableNumber;
                    if (completeTableModalMessage) {
                        completeTableModalMessage.textContent = `Meja ${tableNumber} akan diselesaikan dan dikosongkan.`;
                    }
                    if (confirmCompleteTableBtn) {
                        confirmCompleteTableBtn.dataset.sourceTable = String(tableNumber);
                    }
                    completeTableModal?.show();
                });
            });

            const renderKitchenNotifications = (items) => {
                if (!kitchenNotifList) return;
                if (!items.length) {
                    kitchenNotifList.innerHTML = `<span class="text-white-50 small">Belum ada notifikasi siap.</span>`;
                    return;
                }

                const esc = (v) => String(v || "")
                    .replaceAll("&", "&amp;")
                    .replaceAll("<", "&lt;")
                    .replaceAll(">", "&gt;")
                    .replaceAll("\"", "&quot;");

                kitchenNotifList.innerHTML = items.slice(0, 8).map((n) => `
                    <button class="btn kasir-notif-chip" type="button"
                            data-notif-id="${n.id}"
                            data-order-id="${n.orderId}"
                            data-table="${n.tableNumber || ""}"
                            title="${esc(n.message)}">
                        #${n.orderId} ${n.tableNumber ? `(M${n.tableNumber})` : ""} - ${esc(n.itemSummary || "-")}
                    </button>
                `).join("");
            };

            const ackKitchenNotification = async (notifId) => {
                try {
                    await fetch(`/cashier/notifications/${notifId}/ack`, { method: "POST" });
                } catch (_err) {
                }
            };

            const sendReadyCall = async (tableNumber, message) => {
                const response = await fetch("/Kasir/CallReady", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ tableNumber, message })
                });
                const data = await response.json();
                if (!data || !data.success) {
                    throw new Error((data && data.error) || "Panggilan gagal dikirim.");
                }
                return data;
            };

            const fetchOrderDetail = async (orderId) => {
                const res = await fetch(`/api/orders/${orderId}`, { cache: "no-store" });
                const data = await res.json();
                if (!res.ok || !data?.success || !data?.order) {
                    throw new Error((data && data.error) || "Detail pesanan tidak ditemukan.");
                }
                return data;
            };

            const buildOrderItemsSummary = (order) => {
                const rows = Array.isArray(order?.items) ? order.items : [];
                if (!rows.length) return "-";
                return rows
                    .slice(0, 4)
                    .map((x) => `${Number(x?.quantity || 1)}x ${String(x?.name || "Item")}`)
                    .join(", ");
            };

            const renderReceiptModal = (order, items) => {
                if (!receiptContent) return;
                const rows = Array.isArray(items) ? items : [];
                const bodyRows = rows.length
                    ? rows.map((x) => {
                        const qty = Number(x?.quantity || 0);
                        const unitPrice = Number(x?.unitPrice || 0);
                        const subtotal = Number(x?.subtotal || (qty * unitPrice));
                        const notes = String(x?.notes || "").trim();
                        return `<tr>
                            <td>${escapeHtml(x?.name || "Item")}${notes ? `<div class="small text-white-50">Catatan: ${escapeHtml(notes)}</div>` : ""}</td>
                            <td class="text-center">${qty}</td>
                            <td class="text-end">${formatRupiah(unitPrice)}</td>
                            <td class="text-end">${formatRupiah(subtotal)}</td>
                        </tr>`;
                    }).join("")
                    : `<tr><td colspan="4" class="text-center text-white-50">Detail item tidak tersedia.</td></tr>`;

                const orderCode = order?.nomorPesanan || ("#" + (order?.id || "-"));
                const orderTime = order?.waktuPesan ? new Date(order.waktuPesan).toLocaleString("id-ID") : "-";
                receiptContent.innerHTML = `
                    <div class="border border-secondary rounded p-3">
                        <div class="d-flex justify-content-between flex-wrap gap-2 mb-2">
                            <div>
                                <div class="fw-bold">CafeYo</div>
                                <div class="small text-white-50">Struk Pembayaran</div>
                            </div>
                            <div class="text-end small">
                                <div><strong>${escapeHtml(orderCode)}</strong></div>
                                <div>Meja: ${escapeHtml(order?.nomorMeja || "-")}</div>
                                <div>Status: ${escapeHtml(order?.status || "-")}</div>
                            </div>
                        </div>
                        <div class="small mb-2">Waktu: ${escapeHtml(orderTime)}</div>
                        <div class="table-responsive">
                            <table class="table table-dark table-sm align-middle mb-2">
                                <thead>
                                    <tr>
                                        <th>Item</th>
                                        <th class="text-center">Qty</th>
                                        <th class="text-end">Harga</th>
                                        <th class="text-end">Subtotal</th>
                                    </tr>
                                </thead>
                                <tbody>${bodyRows}</tbody>
                            </table>
                        </div>
                        <div class="d-flex justify-content-end">
                            <div class="text-end">
                                <div>Total</div>
                                <div class="fs-5 fw-bold">${formatRupiah(Number(order?.total || 0))}</div>
                            </div>
                        </div>
                    </div>`;
            };

            const printReceipt = () => {
                if (!receiptContent) return;
                const printWindow = window.open("", "_blank", "width=900,height=700");
                if (!printWindow) {
                    showToast("Gagal", "Popup print diblokir browser.", false);
                    return;
                }
                printWindow.document.write(`
                    <html><head><title>Struk Pembayaran</title>
                    <style>
                        body{font-family:Arial,sans-serif;padding:20px}
                        table{width:100%;border-collapse:collapse}
                        th,td{border:1px solid #d9d9d9;padding:6px;font-size:12px}
                        .text-end{text-align:right}
                        .text-center{text-align:center}
                    </style>
                    </head><body>${receiptContent.innerHTML}</body></html>`);
                printWindow.document.close();
                printWindow.focus();
                printWindow.print();
            };

            const pollKitchenNotifications = async () => {
                try {
                    const res = await fetch("/cashier/notifications", { cache: "no-store" });
                    if (!res.ok) return;
                    const data = await res.json();
                    const rows = data && Array.isArray(data.notifications) ? data.notifications : [];

                    if (kitchenNotifCount) {
                        kitchenNotifCount.textContent = String(rows.length);
                    }

                    rows.forEach((n) => {
                        if (shownKitchenNotif.has(n.id)) return;
                        shownKitchenNotif.add(n.id);
                        showToast("Pesanan Siap", n.message || `Pesanan #${n.orderId} sudah siap.`, true);
                    });

                    renderKitchenNotifications(rows);
                } catch (_e) {
                }
            };

            const renderPendingPayments = (rows) => {
                if (!pendingPaymentsBody) return;
                if (!Array.isArray(rows) || rows.length === 0) {
                    pendingPaymentsBody.innerHTML = '<tr><td colspan="6" class="text-center text-white-50">Tidak ada pesanan menunggu pembayaran.</td></tr>';
                    return;
                }

                pendingPaymentsBody.innerHTML = rows.map((o) => `
                    <tr>
                        <td>${escapeHtml(o.orderCode || ('#' + o.orderId))}</td>
                        <td>${escapeHtml(o.tableNumber || '-')}</td>
                        <td>${escapeHtml(normalizePaymentMethodLabel(o.paymentMethod || '-'))}</td>
                        <td>${escapeHtml(o.paymentStatus || '-')}</td>
                        <td class="text-end">${formatRupiah(Number(o.total || 0))}</td>
                        <td class="d-flex gap-1">
                            <button class="btn btn-success btn-sm pending-confirm-btn" data-order-id="${o.orderId}">Konfirmasi Bayar</button>
                            <button class="btn btn-outline-light btn-sm pending-detail-btn" data-order-id="${o.orderId}">Lihat Detail</button>
                        </td>
                    </tr>
                `).join("");
            };

            const fetchPendingPayments = async () => {
                try {
                    const method = paymentMethodFilter ? String(paymentMethodFilter.value || "").trim() : "";
                    const qs = method ? `?method=${encodeURIComponent(method)}` : "";
                    const res = await fetch(`/Kasir/PendingPayments${qs}`, { cache: "no-store" });
                    const data = await res.json();
                    if (!res.ok || !data?.success) {
                        renderPendingPayments([]);
                        return;
                    }
                    renderPendingPayments(data.orders || []);
                } catch (_e) {
                    renderPendingPayments([]);
                }
            };

            kitchenNotifList?.addEventListener("click", async (event) => {
                const btn = event.target.closest(".kasir-notif-chip");
                if (!btn) return;
                switchKasirPanel("notif");
                const notifId = Number.parseInt(btn.dataset.notifId || "0", 10);
                const orderId = Number.parseInt(btn.dataset.orderId || "0", 10);
                const tableNumber = Number.parseInt(btn.dataset.table || "0", 10);
                if (tableNumber > 0) {
                    triggerCardFx(tableNumber);
                }
                if (notifId <= 0) return;

                try {
                    if (tableNumber > 0) {
                        let message = `Pesanan #${orderId || "-"} untuk Meja ${tableNumber} sudah siap. Silakan dinikmati.`;
                        if (orderId > 0) {
                            try {
                                const detail = await fetchOrderDetail(orderId);
                                const itemSummary = buildOrderItemsSummary({ items: detail.items });
                                if (itemSummary && itemSummary !== "-") {
                                    message = `Pesanan #${orderId} Meja ${tableNumber} siap: ${itemSummary}.`;
                                }
                            } catch (_detailErr) {
                            }
                        }
                        await sendReadyCall(tableNumber, message);
                        count += 1;
                        if (counterEl) {
                            counterEl.textContent = String(count);
                        }
                    }
                    await ackKitchenNotification(notifId);
                    await pollKitchenNotifications();
                    showToast("Panggilan terkirim", `Notifikasi siap untuk Meja ${tableNumber || "-"} sudah dikirim ke customer.`, true);
                } catch (err) {
                    showToast("Gagal", err?.message || "Tidak bisa mengirim panggilan meja.", false);
                }
            });

            pendingPaymentsBody?.addEventListener("click", async (event) => {
                const btn = event.target.closest(".pending-confirm-btn");
                const detailBtn = event.target.closest(".pending-detail-btn");
                if (detailBtn) {
                    const detailOrderId = Number.parseInt(detailBtn.dataset.orderId || "0", 10);
                    if (!detailOrderId) return;
                    try {
                        const detail = await fetchOrderDetail(detailOrderId);
                        renderReceiptModal(detail.order, detail.items);
                        receiptModal?.show();
                    } catch (_err) {
                        showToast("Gagal", "Tidak bisa mengambil detail order.", false);
                    }
                    return;
                }

                if (!btn) return;
                const orderId = Number.parseInt(btn.dataset.orderId || "0", 10);
                if (!orderId) return;
                btn.disabled = true;
                try {
                    const res = await fetch("/Kasir/ConfirmPayment", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ orderId })
                    });
                    const data = await res.json();
                    if (!res.ok || !data?.success) {
                        showToast("Gagal", (data && data.error) || "Gagal konfirmasi bayar.", false);
                        return;
                    }
                    showToast("Berhasil", `Pesanan #${orderId} sudah lunas dan masuk antrean dapur.`, true);
                    await fetchPendingPayments();
                } catch (_err) {
                    showToast("Gagal", "Tidak bisa menghubungi server.", false);
                } finally {
                    btn.disabled = false;
                }
            });

            refreshPendingPaymentsBtn?.addEventListener("click", fetchPendingPayments);
            paymentMethodFilter?.addEventListener("change", fetchPendingPayments);
            kasirSectionNav?.addEventListener("click", (event) => {
                const btn = event.target.closest(".kasir-nav-btn");
                if (!btn) return;
                switchKasirPanel(btn.dataset.kasirTab || "order");
            });

            refreshPaymentBtn?.addEventListener("click", refreshPaymentStatus);

            copyInvoiceBtn?.addEventListener("click", async () => {
                if (!activePaymentInvoice) {
                    showToast("Informasi", "Invoice belum tersedia.", false);
                    return;
                }

                try {
                    await navigator.clipboard.writeText(activePaymentInvoice);
                    showToast("Berhasil", "Kode pembayaran berhasil disalin.", true);
                } catch (_err) {
                    showToast("Gagal", "Tidak bisa menyalin kode pembayaran otomatis.", false);
                }
            });

            printReceiptBtn?.addEventListener("click", printReceipt);

            confirmCompleteTableBtn?.addEventListener("click", async () => {
                const tableNumber = Number.parseInt(confirmCompleteTableBtn.dataset.sourceTable || String(pendingCompleteTableNumber || "0"), 10);
                const sourceButton = tableCompleteButtons.find((x) => Number.parseInt(x.dataset.table || "0", 10) === tableNumber) || null;
                await executeCompleteTable(tableNumber, sourceButton);
            });

            refreshTablesFromApi();
            pollKitchenNotifications();
            fetchPendingPayments();
            window.setInterval(pollKitchenNotifications, 4000);
            window.setInterval(refreshTablesFromApi, 5000);
            window.setInterval(fetchPendingPayments, 7000);
        });
