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

var currentUserUuid = null;

$(function () {
    $.get("/api/alexandria/auth/me", function (me) {
        currentUserUuid = me.userUuid;
        loadUsers();
    });

    $("#confirm-modal-backdrop").on("click", function (e) {
        if (e.target === this) $(this).removeClass("open");
    });

    $("#add-user-btn").on("click", function (e) {
        e.preventDefault();
        openUserModal(null);
    });

    $("#user-modal-close, #user-cancel-btn").on("click", function () {
        $("#user-modal-backdrop").removeClass("open");
    });

    $("#user-modal-backdrop").on("click", function (e) {
        if (e.target === this) $(this).removeClass("open");
    });

    $("#user-form").on("submit", function (e) {
        e.preventDefault();
        saveUser();
    });
});

function loadUsers() {
    $.get("/api/alexandria/users", function (users) {
        var $list = $("#users-list").empty();
        $.each(users, function (i, user) {
            var lastLogin = new Date(user.lastLoginAt).toLocaleDateString() + ' ' + new Date(user.lastLoginAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

            var isSelf = user.userUuid === currentUserUuid;
            var menuItems =
                '<li><a class="dropdown-item" href="#" data-edit-user="' + user.userUuid + '">Edit</a></li>' +
                (isSelf ? '' : '<li><a class="dropdown-item admin-tile-delete" href="#" data-delete-user="' + user.userUuid + '">Delete</a></li>');

            $list.append(
                '<div class="book-list-item">' +
                    '<div class="book-list-info">' +
                        '<div class="book-list-title">' + escapeHtml(user.displayName) + '</div>' +
                        '<div class="book-list-author">' + escapeHtml(user.username) + '</div>' +
                    '</div>' +
                    '<div class="book-list-right">' +
                        '<div class="book-list-read-status">Last Login</div>' +
                        '<div class="book-list-format">' + lastLogin + '</div>' +
                        '<div class="dropdown book-menu-dropdown">' +
                            '<button class="book-menu" data-bs-toggle="dropdown">&middot;&middot;&middot;</button>' +
                            '<ul class="dropdown-menu dropdown-menu-end">' + menuItems + '</ul>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
        });

        $list.on("click", "[data-edit-user]", function (e) {
            e.preventDefault();
            var uuid = $(this).data("edit-user");
            var user = users.find(function (u) { return u.userUuid === uuid; });
            openUserModal(user);
        });

        $list.on("click", "[data-delete-user]", function (e) {
            e.preventDefault();
            var uuid = $(this).data("delete-user");
            showConfirmModal("Delete User", "Delete this user?\nThis action cannot be undone.", function () {
                deleteUser(uuid);
            });
        });
    });
}

function clearUserErrors() {
    $("#user-form .bm-edit-input-invalid").removeClass("bm-edit-input-invalid");
    $("#user-form .bm-edit-error").remove();
    $("#user-error").hide().text("");
}

function openUserModal(user) {
    clearUserErrors();
    if (user) {
        $("#user-modal-title").text("Edit User");
        $("#user-uuid").val(user.userUuid);
        $("#user-username").val(user.username);
        $("#user-password").val("");
        $("#user-displayname").val(user.displayName);
        $(".user-password-hint").removeClass("d-none");
    } else {
        $("#user-modal-title").text("Add User");
        $("#user-uuid").val("");
        $("#user-username").val("");
        $("#user-password").val("");
        $("#user-displayname").val("");
        $(".user-password-hint").addClass("d-none");
    }
    $("#user-modal-backdrop").addClass("open");
}

function saveUser() {
    clearUserErrors();

    var uuid = $("#user-uuid").val();
    var isEdit = !!uuid;
    var valid = true;

    var username = $("#user-username").val().trim();
    if (!username) {
        $("#user-username").addClass("bm-edit-input-invalid");
        $("#user-username").closest(".bm-edit-field").append('<div class="bm-edit-error">Username is required</div>');
        valid = false;
    }

    var password = $("#user-password").val();
    if (!isEdit && !password) {
        $("#user-password").addClass("bm-edit-input-invalid");
        $("#user-password").closest(".bm-edit-field").append('<div class="bm-edit-error">Password is required for new users</div>');
        valid = false;
    }

    var displayName = $("#user-displayname").val().trim();
    if (!displayName) {
        $("#user-displayname").addClass("bm-edit-input-invalid");
        $("#user-displayname").closest(".bm-edit-field").append('<div class="bm-edit-error">Display name is required</div>');
        valid = false;
    }

    if (!valid) return;

    var url = isEdit ? "/api/alexandria/users/" + uuid : "/api/alexandria/users";
    var method = isEdit ? "PUT" : "POST";

    $("#user-save-btn").prop("disabled", true);

    $.ajax({
        url: url,
        method: method,
        contentType: "application/json",
        data: JSON.stringify({ username: username, password: password, displayName: displayName }),
        success: function () {
            $("#user-modal-backdrop").removeClass("open");
            loadUsers();
        },
        error: function (jqXHR) {
            var msg = "Save failed";
            if (jqXHR.responseJSON && jqXHR.responseJSON.error)
                msg = jqXHR.responseJSON.error;
            $("#user-error").text(msg).show();
        },
        complete: function () {
            $("#user-save-btn").prop("disabled", false);
        }
    });
}

function deleteUser(uuid) {
    $.ajax({
        url: "/api/alexandria/users/" + uuid,
        method: "DELETE",
        success: function () {
            loadUsers();
        },
        error: function (jqXHR) {
            var msg = "Failed to delete user.";
            if (jqXHR.responseJSON && jqXHR.responseJSON.error)
                msg = jqXHR.responseJSON.error;
            showMessageModal("Error", msg);
        }
    });
}
