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
                                '<thead><tr><th>Current</th><th>New</th></tr></thead>' +
                                '<tbody></tbody>' +
                            '</table>' +
                        '</div>' +
                        '<hr class="about-divider">' +
                        '<div class="sync-popover-progress"><div class="sync-popover-progress-bar" id="mf-progress-bar" style="width:0%"></div></div>' +
                        '<div class="settings-tile-desc mt-2" id="mf-run-status"></div>' +
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

        plan.operations.forEach(function (op) {
            $body.append(
                '<tr class="mf-row-rename">' +
                    '<td>' + escapeHtml(basename(op.currentPath)) + '</td>' +
                    '<td>' + escapeHtml(basename(op.newPath)) + '</td>' +
                '</tr>'
            );
        });

        plan.conflicts.forEach(function (c) {
            var label = c.kind === "Duplicate" ? "Duplicate target" : "Target exists";
            $body.append(
                '<tr class="mf-row-conflict">' +
                    '<td><em>' + escapeHtml(label) + '</em></td>' +
                    '<td>' + escapeHtml(basename(c.path)) + '</td>' +
                '</tr>'
            );
        });

        plan.skipped.forEach(function (s) {
            var name = escapeHtml(basename(s.currentPath));
            $body.append(
                '<tr class="mf-row-skip">' +
                    '<td>' + name + '</td>' +
                    '<td>' + name + '</td>' +
                '</tr>'
            );
        });

        previewRan = true;
        var canRun = plan.operations.length > 0 && plan.conflicts.length === 0;
        $("#mf-run").prop("disabled", !canRun);
    }

    function basename(path) {
        if (!path) return "";
        var isFolder = path.toLowerCase().endsWith(".mp3audiobook");
        if (isFolder)
            path = path.substring(0, path.length - ".mp3audiobook".length);
        var idx = Math.max(path.lastIndexOf("/"), path.lastIndexOf("\\"));
        var name = idx >= 0 ? path.substring(idx + 1) : path;
        return isFolder ? "/" + name + "/" : "/" + name;
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

    function runRename() {
        $("#mf-run").prop("disabled", true);
        $("#mf-progress-bar").css("width", "0%");
        $("#mf-run-status").text("Starting...");

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
                        if (json.failed) {
                            $("#mf-run-status").text(json.error || "Failed.");
                            $("#mf-run").prop("disabled", false);
                        } else if (json.done) {
                            $("#mf-progress-bar").css("width", "100%");
                            $("#mf-run-status").text("Done. " + json.renamed + " renamed, " + json.skipped + " skipped, " + json.failed.length + " failed.");
                            $("#mf-run").prop("disabled", true);
                            dryRun();
                        } else {
                            var pct = json.total > 0 ? Math.round((json.progress / json.total) * 100) : 0;
                            $("#mf-progress-bar").css("width", pct + "%");
                            $("#mf-run-status").text(json.title + " (" + json.progress + " / " + json.total + ")");
                        }
                    });
                    read();
                });
            }
            read();
        }).catch(function () {
            $("#mf-run-status").text("Request failed.");
            $("#mf-run").prop("disabled", false);
        });
    }

    $(document).on("click", "[data-open-manage-files]", function (e) {
        e.preventDefault();
        $("#manage-files-modal-backdrop").addClass("open");
        invalidatePreview();
        $("#mf-progress-bar").css("width", "0%");
        $("#mf-run-status").text("");
        $("#mf-template-example").text("");
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
