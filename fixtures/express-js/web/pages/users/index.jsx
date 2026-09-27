import { useEffect, useState } from 'react';
import { UserList } from '../../components/UserList';
import { loadUsers } from '../../lib/api';

export default function UsersPage() {
  const [users, setUsers] = useState([]);
  useEffect(() => {
    loadUsers().then(setUsers);
  }, []);
  return <UserList users={users} />;
}
