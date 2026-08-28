use egui::{vec2, Color32, FontId, Rect, Response, RichText, ScrollArea, Sense, Stroke, TextEdit, Ui};
use crate::app::{MyelinApp, ProblemSeverity};
use crate::theme::*;

pub fn render_bottom_panel(app: &mut MyelinApp, ui: &mut Ui) {
    if !app.is_panel_open {
        return;
    }

    // Panel Header (35px)
    ui.horizontal(|ui| {
        ui.spacing_mut().item_spacing = vec2(16.0, 0.0);

        // Terminal Tab Button
        let is_term = app.active_panel_tab == 0;
        let term_color = if is_term { FG_PANEL_ACTIVE } else { FG_PANEL_INACTIVE };
        if ui.add(egui::Button::new(RichText::new("TERMINAL").size(11.0).color(term_color).strong()).frame(false)).clicked() {
            app.active_panel_tab = 0;
        }

        // Output Tab Button
        let is_out = app.active_panel_tab == 1;
        let out_color = if is_out { FG_PANEL_ACTIVE } else { FG_PANEL_INACTIVE };
        if ui.add(egui::Button::new(RichText::new("OUTPUT").size(11.0).color(out_color).strong()).frame(false)).clicked() {
            app.active_panel_tab = 1;
        }

        // Problems Tab Button
        let is_prob = app.active_panel_tab == 2;
        let prob_color = if is_prob { FG_PANEL_ACTIVE } else { FG_PANEL_INACTIVE };
        let prob_label = format!("PROBLEMS ({})", app.problems.len());
        if ui.add(egui::Button::new(RichText::new(prob_label).size(11.0).color(prob_color).strong()).frame(false)).clicked() {
            app.active_panel_tab = 2;
        }

        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            // Close Panel Button
            if ui.add(egui::Button::new(RichText::new("\u{00D7}").size(14.0).color(FG_PANEL_INACTIVE)).frame(false)).clicked() {
                app.is_panel_open = false;
            }

            // Launch External Terminal Button
            if ui.add(egui::Button::new(RichText::new("\u{2197}").size(12.0).color(FG_PANEL_INACTIVE)).frame(false)).clicked() {
                #[cfg(target_os = "windows")]
                let _ = std::process::Command::new("cmd.exe").arg("/c").arg("start").arg("powershell.exe").spawn();
            }
        });
    });

    ui.separator();

    // Poll new terminal output
    if let Some(term) = &app.terminal {
        let output = term.read_available_output();
        if !output.is_empty() {
            app.terminal_output.push_str(&output);
            // Cap output history to prevent unbounded growth
            if app.terminal_output.len() > 100_000 {
                let start = app.terminal_output.len() - 80_000;
                app.terminal_output = app.terminal_output[start..].to_string();
            }
        }
    }

    // Panel Body Content
    match app.active_panel_tab {
        0 => render_terminal_content(app, ui),
        1 => render_output_content(app, ui),
        2 => render_problems_content(app, ui),
        _ => {}
    }
}

fn render_terminal_content(app: &mut MyelinApp, ui: &mut Ui) {
    ScrollArea::vertical()
        .auto_shrink([false; 2])
        .stick_to_bottom(true)
        .show(ui, |ui| {
            ui.add(
                egui::Label::new(
                    RichText::new(&app.terminal_output)
                        .font(FontId::monospace(12.0))
                        .color(Color32::from_rgb(0xCC, 0xCC, 0xCC)),
                )
                .wrap(),
            );

            // Command input row at the bottom of the terminal
            ui.horizontal(|ui| {
                ui.label(RichText::new("PS >").font(FontId::monospace(12.0)).color(ACCENT_BLUE));
                let input_resp = ui.add(
                    TextEdit::singleline(&mut app.terminal_input)
                        .font(FontId::monospace(12.0))
                        .frame(false)
                        .desired_width(ui.available_width() - 20.0),
                );

                if input_resp.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)) {
                    if !app.terminal_input.is_empty() {
                        let cmd = format!("{}\r\n", app.terminal_input);
                        if let Some(term) = &app.terminal {
                            let _ = term.write_input(&cmd);
                        }
                        app.terminal_input.clear();
                        input_resp.request_focus();
                    }
                }
            });
        });
}

fn render_output_content(app: &mut MyelinApp, ui: &mut Ui) {
    ScrollArea::vertical()
        .auto_shrink([false; 2])
        .stick_to_bottom(true)
        .show(ui, |ui| {
            ui.add(
                egui::Label::new(
                    RichText::new(&app.build_output)
                        .font(FontId::monospace(12.0))
                        .color(Color32::from_rgb(0x9C, 0xDC, 0xFE)),
                )
                .wrap(),
            );
        });
}

fn render_problems_content(app: &mut MyelinApp, ui: &mut Ui) {
    if app.problems.is_empty() {
        ui.vertical_centered(|ui| {
            ui.add_space(20.0);
            ui.label(RichText::new("No problems have been detected in the workspace.").size(12.0).color(FG_PANEL_INACTIVE));
        });
        return;
    }

    ScrollArea::vertical().show(ui, |ui| {
        for problem in &app.problems {
            ui.horizontal(|ui| {
                let (icon, color) = match problem.severity {
                    ProblemSeverity::Error => ("\u{26D4}", COLOR_ERROR),     // ⛔
                    ProblemSeverity::Warning => ("\u{26A0}", COLOR_WARNING), // ⚠
                    ProblemSeverity::Info => ("\u{2139}", COLOR_INFO),       // ℹ
                };

                ui.label(RichText::new(icon).color(color).size(12.0));
                ui.label(RichText::new(&problem.message).color(FG_EDITOR).size(12.0));
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let loc = format!("{}:{}:{}", problem.file, problem.line, problem.column);
                    ui.label(RichText::new(loc).color(FG_PANEL_INACTIVE).size(11.0));
                });
            });
            ui.separator();
        }
    });
}
