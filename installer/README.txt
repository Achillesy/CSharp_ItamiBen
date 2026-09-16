ItamiBen — 一袋米要我洗嘞
痛みを感じろ — feel the pain


What it is
==========

A pomodoro clock that will not take your word for it. You pick one of the goals
you wrote yourself, commit to a length, and from then on the program reads the
foreground window once a second. Only the seconds that match your own rules
count. It never pops up, never nags, never congratulates you — there is only
the red on the ring, and the rest block being pushed further and further away.


Where your files live
=====================

    %APPDATA%\ItamiBen\

    ItamiBen.sqlite3  Everything: your configuration (goals, rules, commands,
                      schedule) and your history (observations, sessions, the
                      hours ledger).
    AGENT.md          Written for an AI, not for you. Refreshed every launch.
    itamiben.log      Every configuration change ever applied -- what was asked
                      for, what ran, whether it worked. Plain text.


Configuring it: ask an AI
=========================

There are no configuration files. Goals, matching rules, the command list and
the schedule are rows in the database above, and they are meant to be written
by an AI, not by hand.

If you have a coding assistant with access to your files, point it at that
folder and say what you want:

    Read AGENT.md and set ItamiBen up so only VS Code counts as work.

    Read AGENT.md and have ItamiBen remind me to stand up every hour between
    9 and 6 on weekdays.

If you do not have one, open Settings (the gear) and press the red
"Configure online" button. It shows you your current configuration, lets you
write what you want in plain words, and copies the whole lot to your clipboard.
Paste that into any web AI, bring the answer back, and press Apply.

WARNING: that clipboard text contains your goal names, your reminder texts and
the list of applications on this machine. Delete anything you would rather not
share before you copy -- the box is editable. Window titles and per-second
history are never included.


Rules, and the one trap worth knowing
=====================================

A rule matches the foreground application name, the window title, or both, with
regular expressions. They are CASE SENSITIVE, and the two platforms report
different names: Windows sees "Code.exe" where macOS sees "Code". A rule written
for the wrong one matches nothing at all and does not error -- the ring simply
stays red. The AI is told this, and it can check against the application names
ItamiBen has actually seen on this machine.

ItamiBen gets no special treatment: looking at its own dial counts as off-task,
exactly like looking at anything else.


The alarm's command
===================

The command list is a table in the database. When the alarm fires, ItamiBen runs
the one named by the alarmCommand setting -- but ONLY if you switched "Run
command at alarm" on in the right-click menu, and that switch is OFF every time
the program starts.

Settings shows you the exact command before you flip the switch. It is usually a
shutdown command; you have a right to know what you are arming.


When something looks wrong
==========================

The window will not tell you. That is deliberate.

Hand %APPDATA%\ItamiBen\itamiben.log to an AI and say what you expected.
