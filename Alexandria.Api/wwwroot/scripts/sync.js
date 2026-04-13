// ===== Sync Popover =====
$(function () {
    $("body").append(
        '<div class="sync-popover-backdrop" id="sync-popover-backdrop">' +
            '<div class="sync-popover-anchor"><div class="sync-popover" id="sync-popover">' +
                '<div class="sync-popover-text" id="sync-popover-text">Sync library files for changes.</div>' +
                '<div class="sync-popover-progress" id="sync-popover-progress"><div class="sync-popover-progress-bar" id="sync-popover-progress-bar"></div></div>' +
                '<button class="sync-popover-btn" id="sync-popover-btn">Sync now</button>' +
                '<div class="sync-popover-last-sync" id="sync-popover-last-sync">LAST SYNC: NEVER</div>' +
            '</div></div>' +
        '</div>'
    );

    var syncInProgress = false;
    var syncCompleted = false;

    function formatLastIndexed(isoString) {
        if (!isoString) { return "NEVER"; }
        var d = new Date(isoString);
        var now = new Date();
        var hh = String(d.getHours()).padStart(2, "0");
        var mm = String(d.getMinutes()).padStart(2, "0");
        if (d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate()) {
            return hh + ":" + mm;
        }
        var yyyy = d.getFullYear();
        var mo = String(d.getMonth() + 1).padStart(2, "0");
        var dd = String(d.getDate()).padStart(2, "0");
        return yyyy + "-" + mo + "-" + dd + " " + hh + ":" + mm;
    }

    function openSyncPopover() {
        if (!syncInProgress) {
            $("#sync-popover-text").text("Sync library files for changes.");
            $("#sync-popover-progress-bar").css("width", "0%");
            $("#sync-popover-btn").text("Sync now").prop("disabled", false);
            syncCompleted = false;
            $.get("/api/alexandria/settings", function (data) {
                $("#sync-popover-last-sync").text("LAST SYNC: " + formatLastIndexed(data.lastIndexedTimestamp));
            });
        }
        $("#sync-popover-backdrop").addClass("open");
    }

    function closeSyncPopover() {
        if (syncInProgress) { return; }
        $("#sync-popover-backdrop").removeClass("open");
    }

    window.closeSyncPopover = closeSyncPopover;

    $("#sync-btn").on("click", function () {
        if ($("#sync-popover-backdrop").hasClass("open")) {
            closeSyncPopover();
        } else {
            openSyncPopover();
        }
    });

    $("#sync-popover-backdrop").on("click", function (e) {
        if (e.target === this) { closeSyncPopover(); }
    });

    $("#sync-popover-btn").on("click", function () {
        if (syncInProgress) { return; }

        if (syncCompleted) {
            syncCompleted = false;
            $("#sync-popover-backdrop").removeClass("open");
            if (typeof pageReload === "function" && pageReload) {
                pageReload();
            } else {
                location.reload();
            }
            return;
        }

        syncInProgress = true;
        syncCompleted = false;
        $("#sync-popover-btn").text("Syncing...").prop("disabled", true);
        $("#sync-popover-progress-bar").css("width", "0%");

        var token = localStorage.getItem("alexandria-token");
        var headers = token ? { "Authorization": "Bearer " + token } : {};
        fetch("/api/alexandria/sync", { method: "POST", headers: headers }).then(function (response) {
            if (response.status === 401) {
                localStorage.removeItem("alexandria-token");
                window.location.href = "/login.html";
                return;
            }
            var reader = response.body.getReader();
            var decoder = new TextDecoder();

            function read() {
                reader.read().then(function (result) {
                    if (result.done) { return; }
                    var lines = decoder.decode(result.value).split("\n");
                    lines.forEach(function (line) {
                        if (!line.startsWith("data: ")) { return; }
                        var json = JSON.parse(line.substring(6));
                        if (json.done) {
                            $("#sync-popover-last-sync").text("LAST SYNC: " + formatLastIndexed(json.lastIndexedTimestamp));
                            $("#sync-popover-progress-bar").css("width", "100%");
                            $("#sync-popover-text").text("Sync complete. " + json.bookCount + " books indexed.");
                            $("#sync-popover-btn").text("Close").prop("disabled", false);
                            syncInProgress = false;
                            syncCompleted = true;
                        } else {
                            var pct = Math.round((json.current / json.total) * 100);
                            $("#sync-popover-progress-bar").css("width", pct + "%");
                        }
                    });
                    read();
                });
            }
            read();
        }).catch(function () {
            syncInProgress = false;
            syncCompleted = false;
            $("#sync-popover-text").text("Sync failed. Please try again.");
            $("#sync-popover-btn").text("Sync now").prop("disabled", false);
        });
    });
});
