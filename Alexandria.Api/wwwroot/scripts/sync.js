// ===== Sync Popover =====
$(function () {
    $("body").append(
        '<div class="sync-popover-backdrop" id="sync-popover-backdrop">' +
            '<div class="sync-popover-anchor"><div class="sync-popover" id="sync-popover">' +
                '<div class="sync-popover-text" id="sync-popover-text">Sync library files for changes.</div>' +
                '<div class="sync-popover-progress"><div class="sync-popover-progress-bar" id="sync-popover-progress-bar"></div></div>' +
                '<div id="sync-popover-subtask-row" style="display:none;">' +
                    '<div class="sync-popover-subtask-title" id="sync-popover-subtask-title"></div>' +
                    '<div class="sync-popover-progress"><div class="sync-popover-progress-bar" id="sync-popover-subtask-progress-bar"></div></div>' +
                '</div>' +
                '<button class="sync-popover-btn mt-3" id="sync-popover-btn">Sync now</button>' +
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

    function resetSlots(mainText) {
        $("#sync-popover-text").text(mainText);
        $("#sync-popover-progress-bar").css("width", "0%");
        $("#sync-popover-subtask-row").hide();
    }

    function renderProgress(snapshot) {
        var count = snapshot.count || 0;
        var total = snapshot.total || 0;
        $("#sync-popover-text").text(snapshot.title + " (" + count + " / " + total + ")");
        var pct = total > 0 ? Math.round((count / total) * 100) : 0;
        $("#sync-popover-progress-bar").css("width", pct + "%");
        if (snapshot.subTaskTitle) {
            var subCount = snapshot.subCount || 0;
            var subTotal = snapshot.subTotal || 0;
            $("#sync-popover-subtask-title").text(snapshot.subTaskTitle + " (" + subCount + " / " + subTotal + ")");
            var subPct = subTotal > 0 ? Math.round((subCount / subTotal) * 100) : 0;
            $("#sync-popover-subtask-progress-bar").css("width", subPct + "%");
            $("#sync-popover-subtask-row").show();
        } else {
            $("#sync-popover-subtask-row").hide();
        }
    }

    // Opens a POST /sync SSE stream and pipes events into the popover. The server attaches the
    // client to the in-flight run if one is active; otherwise it enqueues a new run. No client-side
    // branching needed for "already running" — the broker handles attach-or-start symmetrically.
    function attachToSyncStream() {
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
                        if (json.failed) {
                            $("#sync-popover-text").text(json.error);
                            $("#sync-popover-progress-bar").css("width", "0%");
                            $("#sync-popover-subtask-row").hide();
                            $("#sync-popover-btn").text("Close").prop("disabled", false);
                            syncInProgress = false;
                            syncCompleted = true;
                        } else if (json.done) {
                            $("#sync-popover-last-sync").text("LAST SYNC: " + formatLastIndexed(json.lastIndexedTimestamp));
                            $("#sync-popover-progress-bar").css("width", "100%");
                            $("#sync-popover-text").text("Sync complete.");
                            $("#sync-popover-subtask-row").hide();
                            $("#sync-popover-btn").text("Close").prop("disabled", false);
                            syncInProgress = false;
                            syncCompleted = true;
                        } else {
                            renderProgress(json);
                        }
                    });
                    read();
                });
            }
            read();
        }).catch(function () {
            syncInProgress = false;
            syncCompleted = false;
            resetSlots("Sync failed. Please try again.");
            $("#sync-popover-btn").text("Sync now").prop("disabled", false);
        });
    }

    function openSyncPopover() {
        if (syncInProgress) {
            $("#sync-popover-backdrop").addClass("open");
            return;
        }

        resetSlots("Sync library files for changes.");
        $("#sync-popover-btn").text("Sync now").prop("disabled", false);
        syncCompleted = false;

        $.get("/api/alexandria/settings", function (data) {
            $("#sync-popover-last-sync").text("LAST SYNC: " + formatLastIndexed(data.lastIndexedTimestamp));
        });

        $.get("/api/alexandria/library-task/status", function (status) {
            if (!status.running) { return; }
            if (status.kind !== "sync") {
                resetSlots("Another library operation is running. Try again shortly.");
                $("#sync-popover-btn").text("Busy").prop("disabled", true);
                return;
            }
            syncInProgress = true;
            $("#sync-popover-btn").text("Syncing...").prop("disabled", true);
            if (status.snapshot) {
                renderProgress(status.snapshot);
            } else {
                resetSlots("Syncing,");
            }
            attachToSyncStream();
        });

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
        resetSlots("Syncing,");
        $("#sync-popover-btn").text("Syncing...").prop("disabled", true);
        attachToSyncStream();
    });
});
