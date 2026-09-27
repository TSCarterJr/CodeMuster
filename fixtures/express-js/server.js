const express = require('express');
const users = require('./routes/users');

const app = express();

app.use(express.json());
app.use('/api/users', users);

app.get('/health', (req, res) => {
  res.json({ ok: true });
});

app.listen(3000);
