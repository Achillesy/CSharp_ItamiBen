ItamiBen — 痛みを知らせる
痛みを知らせる — it lets you know it hurts


What it is
==========

A pomodoro clock that will not take your word for it. You pick one of the goals
you wrote yourself, commit to a length, and from then on the program reads the
foreground window once a second. Only the seconds that match your own rules
count. It never pops up, never nags, never congratulates you — there is only
the red on the ring, and the rest block being pushed further and further away.


Where your files live
=====================

    %LOCALAPPDATA%\ItamiBen\

    rules.md      What counts as work.
    commands.md   Everything this machine may be made to run, and what the
                  alarm runs.
    schedule.md   Recurring reminders, in standard crontab format.
    layout.md     How wide and how see-through the window is.

    *.sample.md   Pristine reference copies, rewritten every launch. If an AI
                  mangles one of your files, compare against these.

    ItamiBen.sqlite3   What the program itself records: observations, sessions,
                       settings and the hours ledger. You never edit this.


Configuring it: hand a file to an AI
====================================

Each configuration file explains itself -- a short note for you, detailed
instructions for an AI, and the actual settings in a marked block at the end.

Give one whole file to any AI and say what you want:

    Only count VS Code and Chrome when the title mentions GitHub.

    Remind me to stand up every hour between 9 and 6 on weekdays.

It works either way: a coding assistant that can read the folder, or a web chat
you paste the file into. Replace the file with what comes back.

There is nothing else to install and no second document the AI needs -- the
file carries its own instructions.

WARNING: a configuration file contains your goal names and your reminder texts.
Delete anything you would rather not share before pasting it into a web chat.
Window titles and per-second history are never in these files.

Changes take effect within a minute. The exception is layout.md, which is read
once at startup; the file says so.


Rules, and the one trap worth knowing
=====================================

A rule matches the foreground application name, the window title, or both, with
regular expressions. They are CASE SENSITIVE, and the two platforms report
different names: Windows sees "Code.exe" where macOS sees "Code". A rule written
for the wrong one matches nothing at all and does not error -- the ring simply
stays red. rules.md tells the AI this, and shows it how to ask you for the
real name instead of guessing.

ItamiBen gets no special treatment: looking at its own dial counts as off-task,
exactly like looking at anything else.


The alarm's command
===================

The command list is a table in the database. When the alarm fires ItamiBen does
ONE of two things, never both: it rings, or it runs the command named by the
alarmCommand setting.

The switch picks which -- "Run command at alarm" in the right-click menu, or the
Command card in Settings, where it reads Ring / Run. Switched to Run, nothing
rings at all: ringing means "go do it yourself", running the command means "do it
for me". That switch is OFF every time the program starts.

Settings shows you the exact command before you flip the switch. It is usually a
shutdown command; you have a right to know what you are arming.


When something looks wrong
==========================

The window will not tell you. That is deliberate.

Hand %LOCALAPPDATA%\ItamiBen\itamiben.log to an AI and say what you expected.
