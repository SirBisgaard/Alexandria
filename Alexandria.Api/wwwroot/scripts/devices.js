function showConfirmModal(title, message, onConfirm) {
    $("#confirm-modal-title").text(title);
    $("#confirm-modal-message").text(message);
    var $buttons = $("#confirm-modal-buttons").empty();
    $buttons.append('<button class="bm-edit-btn" id="confirm-modal-cancel">Cancel</button>');
    $buttons.append('<button class="bm-edit-btn bm-edit-btn-primary" id="confirm-modal-ok">Confirm</button>');
    $("#confirm-modal-backdrop").addClass("open");

    $("#confirm-modal-ok").on("click", function () {
        $("#confirm-modal-backdrop").removeClass("open");
        onConfirm();
    });
    $("#confirm-modal-cancel, #confirm-modal-close").on("click", function () {
        $("#confirm-modal-backdrop").removeClass("open");
    });
}

function showMessageModal(title, message) {
    $("#confirm-modal-title").text(title);
    $("#confirm-modal-message").text(message);
    var $buttons = $("#confirm-modal-buttons").empty();
    $buttons.append('<button class="bm-edit-btn bm-edit-btn-primary" id="confirm-modal-ok">OK</button>');
    $("#confirm-modal-backdrop").addClass("open");

    $("#confirm-modal-ok, #confirm-modal-close").on("click", function () {
        $("#confirm-modal-backdrop").removeClass("open");
    });
}

$(function () {
    loadDevices();

    $("#confirm-modal-backdrop").on("click", function (e) {
        if (e.target === this) $(this).removeClass("open");
    });

    $("#pair-new-btn").on("click", function (e) {
        e.preventDefault();
        openPairModal();
    });

    $("#pair-modal-close, #pair-cancel-btn").on("click", function () {
        closePairModal();
    });

    $("#pair-modal-backdrop").on("click", function (e) {
        if (e.target === this) closePairModal();
    });

    $("#pair-form").on("submit", function (e) {
        e.preventDefault();
        showPairResult();
    });

    $("#pair-copy-btn").on("click", function () {
        var url = $("#pair-result-url").val();
        navigator.clipboard.writeText(url).then(function () {
            $("#pair-copy-btn").text("Copied!");
            setTimeout(function () { $("#pair-copy-btn").text("Copy"); }, 2000);
        });
    });

    $("#pair-back-btn").on("click", function () {
        $("#pair-result").addClass("d-none");
        $("#pair-form").show();
        $("#pair-modal").removeClass("pair-modal-wide");
    });

    $("#pair-done-btn").on("click", function () {
        saveDevice();
    });

    $("#device-modal-close, #device-cancel-btn").on("click", function () {
        $("#device-modal-backdrop").removeClass("open");
    });

    $("#device-modal-backdrop").on("click", function (e) {
        if (e.target === this) $(this).removeClass("open");
    });

    $("#device-form").on("submit", function (e) {
        e.preventDefault();
        updateDevice();
    });
});

function openPairModal() {
    $("#pair-nickname").removeClass("bm-edit-input-invalid");
    $("#pair-nickname").closest(".bm-edit-field").find(".bm-edit-error").remove();
    $("#pair-error").hide().text("");
    $("#pair-nickname").val("");
    $("#pair-form").show();
    $("#pair-result").addClass("d-none");
    $("#pair-modal").removeClass("pair-modal-wide");
    $("#pair-modal-backdrop").addClass("open");
    $("#pair-nickname").focus();
}

function closePairModal() {
    $("#pair-modal-backdrop").removeClass("open");
}

function showPairResult() {
    $("#pair-nickname").removeClass("bm-edit-input-invalid");
    $("#pair-nickname").closest(".bm-edit-field").find(".bm-edit-error").remove();
    $("#pair-error").hide().text("");

    var nickname = $("#pair-nickname").val().trim();
    if (!nickname) {
        $("#pair-nickname").addClass("bm-edit-input-invalid");
        $("#pair-nickname").closest(".bm-edit-field").append('<div class="bm-edit-error">Nickname is required</div>');
        return;
    }

    $("#pair-save-btn").prop("disabled", true);

    $.get("/api/alexandria/devices/generate-key", function (data) {
        var baseUrl = window.location.origin;
        var deviceUrl = baseUrl + "/api/kobo/" + data.deviceUuid;
        $("#pair-result-url").val(deviceUrl);
        $("#pair-result").data("device-key", data.deviceUuid);
        $("#pair-result").data("nickname", nickname);
        $("#pair-form").hide();
        $("#pair-result").removeClass("d-none");
        $("#pair-modal").addClass("pair-modal-wide");
    }).always(function () {
        $("#pair-save-btn").prop("disabled", false);
    });
}

function saveDevice() {
    var deviceKey = $("#pair-result").data("device-key");
    var nickname = $("#pair-result").data("nickname");

    $("#pair-done-btn").prop("disabled", true);

    $.ajax({
        url: "/api/alexandria/devices",
        method: "POST",
        contentType: "application/json",
        data: JSON.stringify({ nickname: nickname, deviceUuid: deviceKey }),
        success: function () {
            closePairModal();
            loadDevices();
        },
        error: function (jqXHR) {
            var msg = "Failed to save device";
            if (jqXHR.responseJSON && jqXHR.responseJSON.error)
                msg = jqXHR.responseJSON.error;
            $("#pair-error").text(msg).show();
        },
        complete: function () {
            $("#pair-done-btn").prop("disabled", false);
        }
    });
}

function loadDevices() {
    $.get("/api/alexandria/devices", function (devices) {
        var $list = $("#devices-list").empty();

        if (devices.length === 0) {
            $list.append('<div class="admin-empty">No devices paired. Click "Pair New Device" to get started.</div>');
            return;
        }

        $.each(devices, function (i, device) {
            var name = escapeHtml(device.nickname || "Unnamed Device");
            var model = device.modelName ? escapeHtml(device.modelName) : "Not yet synced";
            var firmware = device.firmware ? "FW " + escapeHtml(device.firmware) : "";
            var lastSeen = device.serialNumber
                ? new Date(device.lastRequestAt).toLocaleDateString() + ' ' + new Date(device.lastRequestAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
                : "Never";
            var shortKey = device.deviceUuid;

            var lockHtml = "";
            if (device.isLocked) {
                lockHtml =
                    '<div class="admin-tile-lock">' +
                        '<span class="admin-tile-lock-icon" title="Another device attempted to use this device key. Unlock to restore access for the original device.">' +
                            'Locked' +
                        '</span>' +
                    '</div>';
            }

            var menuItems =
                '<li><a class="dropdown-item" href="#" data-edit-device="' + device.deviceUuid + '">Edit</a></li>' +
                (device.isLocked ? '<li><a class="dropdown-item" href="#" data-unlock-device="' + device.deviceUuid + '">Unlock</a></li>' : '') +
                '<li><a class="dropdown-item admin-tile-delete" href="#" data-delete-device="' + device.deviceUuid + '">Delete</a></li>';

            $list.append(
                '<div class="book-list-item">' +
                    '<div class="book-list-info">' +
                        '<div class="book-list-title">' + name + '</div>' +
                        '<div class="book-list-author">' + model + (firmware ? ' &middot; ' + firmware : '') + '</div>' +
                        '<div class="book-list-format">Serial: ' + (device.serialNumber ? escapeHtml(device.serialNumber) : 'Awaiting first sync') + '</div>' +
                        '<div class="book-list-format">Key: ' + shortKey + '</div>' +
                        lockHtml +
                    '</div>' +
                    '<div class="book-list-right">' +
                        '<div class="book-list-read-status">Last Sync</div>' +
                        '<div class="book-list-format">' + lastSeen + '</div>' +
                        '<div class="dropdown book-menu-dropdown">' +
                            '<button class="book-menu" data-bs-toggle="dropdown">&middot;&middot;&middot;</button>' +
                            '<ul class="dropdown-menu dropdown-menu-end">' + menuItems + '</ul>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
        });

        $list.on("click", "[data-edit-device]", function (e) {
            e.preventDefault();
            var uuid = $(this).data("edit-device");
            var device = devices.find(function (d) { return d.deviceUuid === uuid; });
            openDeviceModal(device);
        });

        $list.on("click", "[data-delete-device]", function (e) {
            e.preventDefault();
            var uuid = $(this).data("delete-device");
            showConfirmModal("Delete Device", "Remove this device?\nIt will no longer be able to sync.", function () {
                deleteDevice(uuid);
            });
        });

        $list.on("click", "[data-unlock-device]", function (e) {
            e.preventDefault();
            var uuid = $(this).data("unlock-device");
            unlockDevice(uuid);
        });
    });
}

function openDeviceModal(device) {
    $("#device-nickname").removeClass("bm-edit-input-invalid");
    $("#device-nickname").closest(".bm-edit-field").find(".bm-edit-error").remove();
    $("#device-error").hide().text("");
    $("#device-uuid").val(device.deviceUuid);
    $("#device-nickname").val(device.nickname || "");
    $("#device-modal-backdrop").addClass("open");
}

function updateDevice() {
    $("#device-nickname").removeClass("bm-edit-input-invalid");
    $("#device-nickname").closest(".bm-edit-field").find(".bm-edit-error").remove();
    $("#device-error").hide().text("");

    var nickname = $("#device-nickname").val().trim();
    if (!nickname) {
        $("#device-nickname").addClass("bm-edit-input-invalid");
        $("#device-nickname").closest(".bm-edit-field").append('<div class="bm-edit-error">Nickname is required</div>');
        return;
    }

    var uuid = $("#device-uuid").val();
    $("#device-save-btn").prop("disabled", true);

    $.ajax({
        url: "/api/alexandria/devices/" + uuid,
        method: "PUT",
        contentType: "application/json",
        data: JSON.stringify({ nickname: nickname }),
        success: function () {
            $("#device-modal-backdrop").removeClass("open");
            loadDevices();
        },
        error: function (jqXHR) {
            var msg = "Save failed";
            if (jqXHR.responseJSON && jqXHR.responseJSON.error)
                msg = jqXHR.responseJSON.error;
            $("#device-error").text(msg).show();
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
            showMessageModal("Error", "Failed to delete device.");
        }
    });
}

function unlockDevice(uuid) {
    $.ajax({
        url: "/api/alexandria/devices/" + uuid + "/unlock",
        method: "POST",
        success: function () {
            loadDevices();
        },
        error: function () {
            showMessageModal("Error", "Failed to unlock device.");
        }
    });
}
