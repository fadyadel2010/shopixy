"""Loopback-only SMTP capture. Accept only this task's reserved QA recipients.

Credential-bearing messages are retained only in approved private temporary storage.
No connection to an external SMTP service is made.
"""
import email
import email.policy
import json
import os
import socketserver
import uuid
from pathlib import Path

OUT = Path(os.environ['LOCALAPPDATA']) / 'Temp/opencode/registration-phone-20261020/mail'
OUT.mkdir(parents=True, exist_ok=True)


class Handler(socketserver.StreamRequestHandler):
    def reply(self, message):
        self.wfile.write((message + '\r\n').encode())
        self.wfile.flush()

    def handle(self):
        self.reply('220 localhost task-only SMTP capture')
        recipients = []
        while raw := self.rfile.readline():
            command = raw.decode('utf-8', errors='replace').strip()
            verb = command.split(' ', 1)[0].upper()
            if verb in ('EHLO', 'HELO'):
                self.reply('250 localhost')
            elif verb == 'MAIL':
                recipients = []
                self.reply('250 OK')
            elif verb == 'RCPT':
                address = command.split(':', 1)[1].strip().strip('<>').lower()
                if address.startswith('regphone20261020-') and address.endswith('@example.test'):
                    recipients.append(address)
                    self.reply('250 OK')
                else:
                    self.reply('550 Only task-owned reserved recipients are accepted')
            elif verb == 'DATA' and recipients:
                self.reply('354 End with a dot')
                data = []
                while (line := self.rfile.readline()) not in (b'.\r\n', b'.\n', b''):
                    data.append(line[1:] if line.startswith(b'..') else line)
                content = b''.join(data)
                message = email.message_from_bytes(content, policy=email.policy.default)
                for recipient in recipients:
                    (OUT / (recipient.split('@')[0] + '.eml')).write_bytes(content)
                print(json.dumps({'captured': recipients, 'subject': message['Subject'], 'id': str(uuid.uuid4())}), flush=True)
                self.reply('250 Captured locally; no external delivery')
            elif verb == 'QUIT':
                self.reply('221 Bye')
                break
            elif verb in ('RSET', 'NOOP'):
                recipients = []
                self.reply('250 OK')
            else:
                self.reply('502 Unsupported')


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True


with Server(('127.0.0.1', 2526), Handler) as server:
    print('Task-only SMTP capture listening on 127.0.0.1:2526', flush=True)
    server.serve_forever()
