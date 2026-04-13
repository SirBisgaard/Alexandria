$(function () {
    loadUsers();

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
            var lastLogin = new Date(user.lastLoginAt).toLocaleDateString();
            $list.append(
                '<div class="admin-tile">' +
                    '<div class="admin-tile-info">' +
                        '<div class="admin-tile-title">' + escapeHtml(user.displayName) + '</div>' +
                        '<div class="admin-tile-subtitle">' + escapeHtml(user.username) + '</div>' +
                        '<div class="admin-tile-meta">Last login: ' + lastLogin + '</div>' +
                    '</div>' +
                    '<div class="admin-tile-actions">' +
                        '<button class="bm-edit-btn" data-edit-user="' + user.userUuid + '">Edit</button>' +
                        '<button class="bm-edit-btn admin-tile-delete" data-delete-user="' + user.userUuid + '">Delete</button>' +
                    '</div>' +
                '</div>'
            );
        });

        $list.on("click", "[data-edit-user]", function () {
            var uuid = $(this).data("edit-user");
            var user = users.find(function (u) { return u.userUuid === uuid; });
            openUserModal(user);
        });

        $list.on("click", "[data-delete-user]", function () {
            var uuid = $(this).data("delete-user");
            if (confirm("Delete this user?"))
                deleteUser(uuid);
        });
    });
}

function openUserModal(user) {
    $("#user-error").text("");
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
    var uuid = $("#user-uuid").val();
    var data = {
        username: $("#user-username").val(),
        password: $("#user-password").val(),
        displayName: $("#user-displayname").val()
    };

    var isEdit = !!uuid;
    var url = isEdit ? "/api/alexandria/users/" + uuid : "/api/alexandria/users";
    var method = isEdit ? "PUT" : "POST";

    if (!isEdit && !data.password) {
        $("#user-error").text("Password is required for new users.");
        return;
    }

    $("#user-save-btn").prop("disabled", true);

    $.ajax({
        url: url,
        method: method,
        contentType: "application/json",
        data: JSON.stringify(data),
        success: function () {
            $("#user-modal-backdrop").removeClass("open");
            loadUsers();
        },
        error: function (jqXHR) {
            var msg = "Save failed";
            if (jqXHR.responseJSON && jqXHR.responseJSON.error)
                msg = jqXHR.responseJSON.error;
            $("#user-error").text(msg);
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
            var msg = "Delete failed";
            if (jqXHR.responseJSON && jqXHR.responseJSON.error)
                msg = jqXHR.responseJSON.error;
            alert(msg);
        }
    });
}
