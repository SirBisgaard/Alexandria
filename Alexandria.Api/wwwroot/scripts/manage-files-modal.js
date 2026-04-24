// ===== Manage Files Modal =====
$(function () {
    $("body").append(
        '<div class="book-modal-backdrop" id="manage-files-modal-backdrop">' +
            '<div class="book-modal" id="manage-files-modal">' +
                '<div class="book-modal-title-bar">Manage Files</div>' +
                '<button class="book-modal-close" id="manage-files-close">&times;</button>' +
                '<div class="manage-files-content">' +
                    '<div class="manage-files-section">' +
                        '<div class="manage-files-row">' +
                            '<div class="settings-tile-label">Preview <span class="settings-tile-desc" id="mf-preview-summary"></span></div>' +
                        '</div>' +
                        '<div class="manage-files-table-wrap">' +
                            '<table class="manage-files-table" id="mf-table-preview">' +
                                '<thead><tr><th>Current</th><th>New</th><th>Type</th></tr></thead>' +
                                '<tbody></tbody>' +
                            '</table>' +
                        '</div>' +
                        '<hr class="about-divider">' +
                        '<div class="sync-popover-progress"><div class="sync-popover-progress-bar" id="mf-progress-bar" style="width:0%"></div></div>' +
                        '<div class="settings-tile-desc mt-2" id="mf-run-status"></div>' +
                        '<div id="mf-subtask-row" style="display:none;">' +
                            '<div class="settings-tile-desc mt-2" id="mf-subtask-title"></div>' +
                            '<div class="sync-popover-progress"><div class="sync-popover-progress-bar" id="mf-subtask-progress-bar" style="width:0%"></div></div>' +
                        '</div>' +
                        '<div class="manage-files-row">' +
                            '<div class="mf-template-info">' +
                                '<span class="mf-template-example" id="mf-template-example"></span>' +
                                '<span class="mf-template-display" id="mf-template-display"></span>' +
                            '</div>' +
                            '<div class="manage-files-actions">' +
                                '<button class="bm-edit-btn" id="mf-dry-run">Preview</button>' +
                                '<button class="bm-edit-btn bm-edit-btn-primary" id="mf-run" disabled>Update</button>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

    var previewRan = false;

    function invalidatePreview() {
        previewRan = false;
        $("#mf-run").prop("disabled", true);
        $("#mf-table-preview tbody").empty();
        $("#mf-preview-summary").text("");
    }

    function renderPlan(plan) {
        $("#mf-preview-summary").text(
            plan.operations.length + " rename · " +
            plan.conflicts.length + " conflict · " +
            plan.skipped.length + " skip"
        );

        var $body = $("#mf-table-preview tbody").empty();

        var operations = plan.operations.slice().sort(function (a, b) {
            return sortKey(a.newPath).localeCompare(sortKey(b.newPath));
        });
        operations.forEach(function (op) {
            $body.append(
                '<tr class="mf-row-rename">' +
                    '<td>' + escapeHtml(basename(op.currentPath)) + '</td>' +
                    '<td>' + escapeHtml(basename(op.newPath)) + '</td>' +
                    '<td>' + escapeHtml(fileType(op.newPath)) + '</td>' +
                '</tr>'
            );
        });

        var conflicts = plan.conflicts.slice().sort(function (a, b) {
            return sortKey(a.path).localeCompare(sortKey(b.path));
        });
        conflicts.forEach(function (c) {
            var label = c.kind === "Duplicate" ? "Duplicate target" : "Target exists";
            $body.append(
                '<tr class="mf-row-conflict">' +
                    '<td><em>' + escapeHtml(label) + '</em></td>' +
                    '<td>' + escapeHtml(basename(c.path)) + '</td>' +
                    '<td>' + escapeHtml(fileType(c.path)) + '</td>' +
                '</tr>'
            );
        });

        var skipped = plan.skipped.slice().sort(function (a, b) {
            return sortKey(a.currentPath).localeCompare(sortKey(b.currentPath));
        });
        skipped.forEach(function (s) {
            var name = escapeHtml(basename(s.currentPath));
            $body.append(
                '<tr class="mf-row-skip">' +
                    '<td>' + name + '</td>' +
                    '<td>' + name + '</td>' +
                    '<td>' + escapeHtml(fileType(s.currentPath)) + '</td>' +
                '</tr>'
            );
        });

        previewRan = true;
        var canRun = plan.operations.length > 0 && plan.conflicts.length === 0;
        $("#mf-run").prop("disabled", !canRun);
    }

    function basename(path) {
        if (!path) return "";
        var idx = Math.max(path.lastIndexOf("/"), path.lastIndexOf("\\"));
        var name = idx >= 0 ? path.substring(idx + 1) : path;
        var isFolder = !/\.(epub|m4b)$/i.test(name);
        return isFolder ? "/" + name + "/" : "/" + name;
    }

    function fileType(path) {
        if (!path) return "";
        var lower = path.toLowerCase();
        if (lower.endsWith(".epub")) return "EPUB";
        if (lower.endsWith(".m4b")) return "M4B";
        return "MP3";
    }

    function sortKey(path) {
        return basename(path).toLowerCase();
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c];
        });
    }

    function dryRun() {
        $("#mf-run-status").text("");
        $.ajax({
            url: "/api/alexandria/file-management/dry-run",
            type: "POST",
            contentType: "application/json",
            data: "",
            success: function (plan) {
                renderPlan(plan);
            },
            error: function (jqXHR) {
                var msg = (jqXHR.responseJSON && jqXHR.responseJSON.error) || "Dry run failed.";
                $("#mf-run-status").text(msg);
            }
        });
    }

    function renderProgress(snapshot) {
        var count = snapshot.count || 0;
        var total = snapshot.total || 0;
        var pct = total > 0 ? Math.round((count / total) * 100) : 0;
        $("#mf-progress-bar").css("width", pct + "%");
        $("#mf-run-status").text(snapshot.title + " (" + count + " / " + total + ")");
        if (snapshot.subTaskTitle) {
            var subCount = snapshot.subCount || 0;
            var subTotal = snapshot.subTotal || 0;
            $("#mf-subtask-title").text(snapshot.subTaskTitle + " (" + subCount + " / " + subTotal + ")");
            var subPct = subTotal > 0 ? Math.round((subCount / subTotal) * 100) : 0;
            $("#mf-subtask-progress-bar").css("width", subPct + "%");
            $("#mf-subtask-row").show();
        } else {
            $("#mf-subtask-row").hide();
        }
    }

    function openStream() {
        $("#mf-dry-run").prop("disabled", true);
        $("#mf-run").prop("disabled", true);

        var token = localStorage.getItem("alexandria-token");
        var headers = token ? { "Authorization": "Bearer " + token } : {};
        fetch("/api/alexandria/file-management", { method: "POST", headers: headers }).then(function (response) {
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
                            if (typeof json.failed === 'boolean') {
                                $("#mf-run-status").text(json.error || "Failed.");
                                $("#mf-subtask-row").hide();
                                $("#mf-dry-run").prop("disabled", false);
                            } else {
                                $("#mf-progress-bar").css("width", "100%");
                                $("#mf-run-status").text("Done. " + json.renamed + " renamed, " + json.skipped + " skipped, " + json.failed.length + " failed.");
                                $("#mf-subtask-row").hide();
                                $("#mf-dry-run").prop("disabled", false);
                            }
                        } else {
                            renderProgress(json);
                        }
                    });
                    read();
                });
            }
            read();
        }).catch(function () {
            $("#mf-run-status").text("Request failed.");
            $("#mf-dry-run").prop("disabled", false);
        });
    }

    function runRename() {
        $("#mf-progress-bar").css("width", "0%");
        $("#mf-run-status").text("Starting...");
        openStream();
    }

    function loadTemplateAndExample() {
        $.get("/api/alexandria/settings", function (data) {
            var template = data.fileRenameTemplate || "";
            $("#mf-template-display").text(template);
            $.ajax({
                url: "/api/alexandria/settings/preview-template",
                type: "POST",
                contentType: "application/json",
                data: JSON.stringify({ template: template }),
                success: function (res) {
                    $("#mf-template-example").text(res.result);
                },
                error: function (jqXHR) {
                    var msg = (jqXHR.responseJSON && jqXHR.responseJSON.error) || "Invalid template.";
                    $("#mf-template-example").text(msg);
                }
            });
        });
    }

    $(document).on("click", "[data-open-manage-files]", function (e) {
        e.preventDefault();
        $("#manage-files-modal-backdrop").addClass("open");
        invalidatePreview();
        $("#mf-progress-bar").css("width", "0%");
        $("#mf-run-status").text("");
        $("#mf-subtask-row").hide();
        $("#mf-template-example").text("");
        $("#mf-dry-run").prop("disabled", false);
        loadTemplateAndExample();

        $.get("/api/alexandria/library-task/status", function (status) {
            if (!status.running) {
                return;
            }
            if (status.kind !== "file-management") {
                $("#mf-run-status").text("Library sync is running. Try again shortly.");
                $("#mf-dry-run").prop("disabled", true);
                return;
            }
            if (status.snapshot) {
                renderProgress(status.snapshot);
            } else {
                $("#mf-run-status").text("Renaming in progress...");
            }
            openStream();
        });
    });

    $("#manage-files-close").on("click", function () {
        $("#manage-files-modal-backdrop").removeClass("open");
    });

    $("#manage-files-modal-backdrop").on("click", function (e) {
        if (e.target === this) { $("#manage-files-modal-backdrop").removeClass("open"); }
    });

    $("#mf-dry-run").on("click", dryRun);
    $("#mf-run").on("click", runRename);

    $(document).on("keydown", function (e) {
        if (e.key === "Escape" && $("#manage-files-modal-backdrop").hasClass("open"))
            $("#manage-files-modal-backdrop").removeClass("open");
    });
});
