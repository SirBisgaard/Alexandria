var pairingTimer = null;
var pairingActiveUntil = null;

$(function () {
    loadDevices();
    checkPairingStatus();

    $("#pairing-menu-item").on("click", function (e) {
        e.preventDefault();
        togglePairing();
    });

    $("#device-modal-close, #device-cancel-btn").on("click", function () {
        $("#device-modal-backdrop").removeClass("open");
    });

    $("#device-modal-backdrop").on("click", function (e) {
        if (e.target === this) $(this).removeClass("open");
    });

    $("#device-form").on("submit", function (e) {
        e.preventDefault();
        saveDevice();
    });
});

function loadDevices() {
    $.get("/api/alexandria/devices", function (devices) {
        var $list = $("#devices-list").empty();

        if (devices.length === 0) {
            $list.append('<div class="admin-empty" id="devices-empty"></div>');
            renderEmptyState();
            return;
        }

        $.each(devices, function (i, device) {
            var name = device.modelName || "Unknown Device";
            var lastSeen = new Date(device.lastRequestAt).toLocaleDateString();
            var shortId = device.deviceId.substring(0, 12) + "...";

            $list.append(
                '<div class="admin-tile">' +
                    '<div class="admin-tile-info">' +
                        '<div class="admin-tile-title">' + escapeHtml(name) + '</div>' +
                        '<div class="admin-tile-subtitle">' +
                            (device.modelCode ? device.modelCode + ' ' : '') +
                            (device.firmware ? 'FW ' + escapeHtml(device.firmware) : '') +
                        '</div>' +
                        '<div class="admin-tile-meta">ID: ' + shortId + '</div>' +
                        '<div class="admin-tile-meta">Last seen: ' + lastSeen + '</div>' +
                    '</div>' +
                    '<div class="admin-tile-actions">' +
                        '<button class="bm-edit-btn" data-edit-device="' + device.deviceUuid + '">Edit</button>' +
                        '<button class="bm-edit-btn admin-tile-delete" data-delete-device="' + device.deviceUuid + '">Delete</button>' +
                    '</div>' +
                '</div>'
            );
        });

        $list.on("click", "[data-edit-device]", function () {
            var uuid = $(this).data("edit-device");
            var device = devices.find(function (d) { return d.deviceUuid === uuid; });
            openDeviceModal(device);
        });

        $list.on("click", "[data-delete-device]", function () {
            var uuid = $(this).data("delete-device");
            if (confirm("Unpair this device? It will need to re-pair to sync."))
                deleteDevice(uuid);
        });
    });
}

function renderEmptyState() {
    var $empty = $("#devices-empty");
    if ($empty.length === 0) return;

    if (pairingActiveUntil) {
        var remaining = Math.max(0, Math.floor((pairingActiveUntil - Date.now()) / 1000));
        if (remaining > 0) {
            var min = Math.floor(remaining / 60);
            var sec = remaining % 60;
            var clock = min + ":" + (sec < 10 ? "0" : "") + sec;
            $empty.html(
                '<div class="admin-empty-title">Pairing mode is active</div>' +
                '<div class="admin-empty-body">Connect your Kobo device now &mdash; it will be picked up automatically.</div>' +
                '<div class="admin-empty-countdown">' + clock + ' remaining</div>'
            );
            return;
        }
    }
    $empty.text('No devices paired. Enable pairing, then connect a Kobo device.');
}

function openDeviceModal(device) {
    $("#device-error").text("");
    $("#device-uuid").val(device.deviceUuid);
    $("#device-modelname").val(device.modelName || "");
    $("#device-modal-backdrop").addClass("open");
}

function saveDevice() {
    var uuid = $("#device-uuid").val();
    $("#device-save-btn").prop("disabled", true);

    $.ajax({
        url: "/api/alexandria/devices/" + uuid,
        method: "PUT",
        contentType: "application/json",
        data: JSON.stringify({ modelName: $("#device-modelname").val() }),
        success: function () {
            $("#device-modal-backdrop").removeClass("open");
            loadDevices();
        },
        error: function (jqXHR) {
            var msg = "Save failed";
            if (jqXHR.responseJSON && jqXHR.responseJSON.error)
                msg = jqXHR.responseJSON.error;
            $("#device-error").text(msg);
        },
        complete: function () {
            $("#device-save-btn").prop("disabled", false);
        }
    });
}

function deleteDevice(uuid) {
    $.ajax({
        url: "/api/alexandria/devices/" + uuid,
        method: "DELETE",
        success: function () {
            loadDevices();
        },
        error: function () {
            alert("Delete failed");
        }
    });
}

function togglePairing() {
    $.get("/api/alexandria/devices/pairing", function (status) {
        if (status.enabled) {
            $.ajax({ url: "/api/alexandria/devices/pairing", method: "DELETE", success: checkPairingStatus });
        } else {
            $.ajax({ url: "/api/alexandria/devices/pairing", method: "POST", success: checkPairingStatus });
        }
    });
}

function checkPairingStatus() {
    $.get("/api/alexandria/devices/pairing", function (status) {
        if (status.enabled) {
            pairingActiveUntil = new Date(status.expiresAt);
            updatePairingDisplay();
            if (pairingTimer) clearInterval(pairingTimer);
            pairingTimer = setInterval(updatePairingDisplay, 1000);
        } else {
            pairingActiveUntil = null;
            if (pairingTimer) { clearInterval(pairingTimer); pairingTimer = null; }
            $("#pairing-menu-item").text("Enable Pairing");
            renderEmptyState();
        }
    });
}

function updatePairingDisplay() {
    if (!pairingActiveUntil) return;
    var remaining = Math.max(0, Math.floor((pairingActiveUntil - Date.now()) / 1000));
    if (remaining <= 0) {
        pairingActiveUntil = null;
        if (pairingTimer) { clearInterval(pairingTimer); pairingTimer = null; }
        $("#pairing-menu-item").text("Enable Pairing");
        $("#pairing-status").text("");
        loadDevices();
        return;
    }
    $("#pairing-menu-item").text("Disable Pairing");
    renderEmptyState();
}
