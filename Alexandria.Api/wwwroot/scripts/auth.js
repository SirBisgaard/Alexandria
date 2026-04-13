$(function () {
    var TOKEN_KEY = "alexandria-token";

    $.ajaxPrefilter(function (options, originalOptions, jqXHR) {
        var token = localStorage.getItem(TOKEN_KEY);
        if (token)
            jqXHR.setRequestHeader("Authorization", "Bearer " + token);
    });

    $(document).ajaxError(function (event, jqXHR) {
        if (jqXHR.status === 401) {
            localStorage.removeItem(TOKEN_KEY);
            window.location.href = "/login.html";
        }
    });

    $.get("/api/alexandria/auth/me")
        .fail(function (jqXHR) {
            if (jqXHR.status === 401)
                window.location.href = "/login.html";
        });
});

function logout() {
    localStorage.removeItem("alexandria-token");
    window.location.href = "/login.html";
}
