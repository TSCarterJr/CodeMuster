const express = require('express');
const { listUsers, findUser } = require('../services/users');

const router = express.Router();

router.get('/', async (req, res) => {
  res.json(await listUsers());
});

router.get('/:id', getUser);

async function getUser(req, res) {
  const user = await findUser(Number(req.params.id));
  if (user === undefined) {
    res.status(404).end();
    return;
  }

  res.json(user);
}

module.exports = router;
