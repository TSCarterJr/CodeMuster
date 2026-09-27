import { useEffect, useState } from 'react';
import { loadUser } from '../../lib/api';

const UserPage = ({ id }) => {
  const [user, setUser] = useState(null);
  useEffect(() => {
    loadUser(id).then(setUser);
  }, [id]);
  return <h1>{user === null ? 'Loading' : user.name}</h1>;
};

export default UserPage;
